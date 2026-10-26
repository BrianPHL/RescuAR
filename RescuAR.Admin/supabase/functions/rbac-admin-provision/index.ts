/**
 * Supabase Edge Function: RescuAR Superadmin-only user provisioning.
 * Passwords are never logged or stored outside Supabase Auth.
 * JWT authentication is checked explicitly inside the function, even if
 * platform verify_jwt is disabled for asymmetric-key projects.
 */
import { createClient } from 'npm:@supabase/supabase-js@2';

const url = Deno.env.get('SUPABASE_URL') ?? '';
const serviceRoleKey = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? '';
const allowedOrigins = (Deno.env.get('RESCUAR_ADMIN_ALLOWED_ORIGINS') ?? '')
  .split(',').map((value) => value.trim()).filter(Boolean);

const jsonResponse = (status: number, body: Record<string, unknown>, origin: string | null) =>
  new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
      'Vary': 'Origin',
      ...(origin && allowedOrigins.includes(origin)
        ? { 'Access-Control-Allow-Origin': origin, 'Access-Control-Allow-Headers': 'authorization, apikey, content-type, x-client-info', 'Access-Control-Allow-Methods': 'POST, OPTIONS' }
        : {}),
    },
  });

function makeTemporaryPassword(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  const random = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  return `R!8a-${random}`; // Includes uppercase, lowercase, number and symbol.
}
async function passwordSha256(value: string): Promise<string> {
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}
function strongPassword(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 12 && value.length <= 128 &&
    /[A-Z]/.test(value) && /[a-z]/.test(value) && /\d/.test(value) && /[^a-zA-Z0-9]/.test(value);
}

Deno.serve(async (req: Request) => {
  const origin = req.headers.get('Origin');
  // The origin check is defense in depth, not authorization. Require a
  // configured allowlist to avoid deploying this highly privileged API open.
  if (!url || !serviceRoleKey || allowedOrigins.length === 0) {
    return jsonResponse(503, { error: 'Admin provisioning is not configured.' }, origin);
  }
  if (!origin || !allowedOrigins.includes(origin)) {
    return jsonResponse(403, { error: 'Origin not allowed.' }, origin);
  }
  if (req.method === 'OPTIONS') return jsonResponse(200, {}, origin);
  if (req.method !== 'POST') return jsonResponse(405, { error: 'Method not allowed.' }, origin);
  const authorization = req.headers.get('Authorization') || '';
  if (!authorization.startsWith('Bearer ')) return jsonResponse(401, { error: 'Sign in first.' }, origin);
  const token = authorization.slice(7);
  if (!token || token.length > 8192) return jsonResponse(401, { error: 'Invalid session.' }, origin);
  const admin = createClient(url, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const { data: userData, error: userError } = await admin.auth.getUser(token);
  if (userError || !userData.user) return jsonResponse(401, { error: 'Invalid or expired session.' }, origin);
  const actor = userData.user.id;
  let body: Record<string, unknown>;
  try {
    if (Number(req.headers.get('content-length') || 0) > 8192) throw new Error('Too large');
    const received = await req.json();
    if (!received || typeof received !== 'object' || Array.isArray(received)) throw new Error('Invalid JSON');
    body = received;
  } catch {
    return jsonResponse(400, { error: 'Invalid request.' }, origin);
  }

  if (body.action === 'create_admin') {
    // Never authorize creation using a client-provided role or permission claim.
    const [{ data: superadmin, error: superError }, { data: setupStatus, error: setupError }] = await Promise.all([
      admin.from('rbac_admin_assignments').select('user_id').eq('user_id', actor).eq('role_id', 'superadmin').maybeSingle(),
      admin.from('rbac_password_setup').select('must_change_password').eq('user_id', actor).maybeSingle(),
    ]);
    if (superError || setupError) return jsonResponse(503, { error: 'Unable to verify administrator permission.' }, origin);
    if (!superadmin || setupStatus?.must_change_password) return jsonResponse(403, { error: 'Superadmin access required.' }, origin);

    const email = typeof body.email === 'string' ? body.email.trim().toLowerCase() : '';
    const role = typeof body.role === 'string' ? body.role : '';
    if (email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || !/^[a-z][a-z0-9_]{2,60}$/.test(role) || role === 'superadmin') {
      return jsonResponse(400, { error: 'Provide a valid email and non-Superadmin role.' }, origin);
    }
    const { data: existingRole, error: roleError } = await admin.from('rbac_roles').select('id').eq('id', role).maybeSingle();
    if (roleError || !existingRole) return jsonResponse(400, { error: 'The selected role does not exist.' }, origin);

    const password = makeTemporaryPassword();
    const { data: created, error: creationError } = await admin.auth.admin.createUser({
      email, password, email_confirm: true,
    });
    if (creationError || !created.user) {
      return jsonResponse(409, { error: 'Unable to create the account. The email may already be registered.' }, origin);
    }
    const userId = created.user.id;
    const { error: registrationError } = await admin.rpc('rbac_register_provisioned_admin', {
      p_user_id: userId, p_role: role, p_actor: actor, p_password_sha256: await passwordSha256(password),
    });
    if (registrationError) {
      // Compensating action if assignment fails; prevent orphaned Auth user.
      const { error: rollbackError } = await admin.auth.admin.deleteUser(userId);
      if (rollbackError) console.error('RBAC provisioning rollback failed; manual account review required.');
      return jsonResponse(500, { error: 'Administrator registration failed. Check the server audit and Auth users before retrying.' }, origin);
    }
    return jsonResponse(201, { email, role, temporaryPassword: password, mustChangePassword: true }, origin);
  }

  if (body.action === 'complete_first_login') {
    const { data: setup, error: setupError } = await admin.from('rbac_password_setup')
      .select('must_change_password, temporary_password_sha256').eq('user_id', actor).maybeSingle();
    if (setupError) return jsonResponse(503, { error: 'Unable to verify password setup status.' }, origin);
    if (!setup?.must_change_password) return jsonResponse(403, { error: 'No pending password setup for this account.' }, origin);
    if (!strongPassword(body.newPassword)) {
      return jsonResponse(400, { error: 'Use 12–128 characters with uppercase, lowercase, number, and symbol.' }, origin);
    }
    if (setup.temporary_password_sha256 === await passwordSha256(body.newPassword)) {
      return jsonResponse(400, { error: 'New password must differ from the temporary password.' }, origin);
    }
    const { error: passwordError } = await admin.auth.admin.updateUserById(actor, { password: body.newPassword });
    if (passwordError) return jsonResponse(400, { error: 'Password update failed. Try a different password.' }, origin);
    const { error: completionError } = await admin.rpc('rbac_complete_password_setup', { p_user_id: actor });
    if (completionError) return jsonResponse(503, { error: 'Password updated, but account activation was not finalized. Sign in with the new password and contact the Superadmin.' }, origin);
    return jsonResponse(200, { updated: true }, origin);
  }
  return jsonResponse(400, { error: 'Unknown action.' }, origin);
});
