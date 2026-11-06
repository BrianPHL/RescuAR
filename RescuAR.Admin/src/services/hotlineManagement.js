// Browser-side guards improve feedback; Supabase RLS is the write authority.
export class HotlinePermissionError extends Error {
  constructor(message) {
    super(message);
    this.name = 'HotlinePermissionError';
  }
}

const allowedPhone = /^\+?[\d\s().#*-]{2,32}$/;
const isValidPhone = (value) => allowedPhone.test(value) && (value.match(/\d/g) || []).length >= 2;
const validEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const clean = (value, maxLength) => String(value ?? '').trim().slice(0, maxLength + 1);

export function buildHotlinePayload(form) {
  const agency = clean(form.agency, 150);
  const primaryNumber = clean(form.primaryNumber, 32);
  const alternativeNumber = clean(form.alternativeNumber, 32);
  const email = clean(form.email, 254);
  const category = clean(form.category, 100) || 'Uncategorized';
  const availability = clean(form.availability, 60) || '24/7';
  const coverage = clean(form.coverage, 120) || 'Citywide';

  if (!agency || agency.length > 150) throw new Error('Enter an agency name (maximum 150 characters).');
  if (!isValidPhone(primaryNumber)) throw new Error('Enter a valid primary hotline number.');
  if (alternativeNumber && !isValidPhone(alternativeNumber)) throw new Error('Enter a valid alternative hotline number.');
  if (email && (email.length > 254 || !validEmail.test(email))) throw new Error('Enter a valid email address.');
  if (category.length > 100 || availability.length > 60 || coverage.length > 120) {
    throw new Error('One or more hotline details exceed the permitted length.');
  }
  return {
    agency,
    primary_number: primaryNumber,
    alternative_number: alternativeNumber || null,
    category,
    availability,
    email: email || null,
    coverage,
  };
}

export async function requireHotlineWriteAccess(client) {
  // getUser validates the current token with Supabase Auth (not local session cache).
  const { data: userData, error: authError } = await client.auth.getUser();
  if (authError || !userData?.user?.id) {
    throw new HotlinePermissionError('Your session is no longer valid. Please sign in again.');
  }

  // Check the current database assignment, not roles cached at initial sign-in.
  const { data: allowed, error: permissionError } = await client.rpc('rbac_can', {
    p_permission: 'hotline.manage',
  });
  if (permissionError) {
    throw new HotlinePermissionError('Unable to verify hotline management access. Please retry.');
  }
  if (allowed !== true) {
    throw new HotlinePermissionError('You do not have permission to manage emergency hotlines.');
  }
}

function checkResult(result, action) {
  if (result.error) {
    if (result.error.code === '42501' || result.error.code === 'PGRST301') {
      throw new HotlinePermissionError('Hotline update denied. Your session or assigned role may have changed.');
    }
    throw new Error(`Unable to ${action} hotline: ${result.error.message || 'database error'}`);
  }
  // UPDATE/DELETE may silently affect zero rows under RLS or when the row is gone.
  if (!Array.isArray(result.data) || result.data.length !== 1) {
    throw new Error('The hotline was not changed. Refresh the list and verify your access.');
  }
  return result.data[0];
}

export async function createHotline(client, form) {
  const payload = buildHotlinePayload(form);
  await requireHotlineWriteAccess(client);
  const result = await client.from('emergency_hotlines').insert([payload]).select('id');
  return checkResult(result, 'add');
}

export async function editHotline(client, id, form) {
  if (id === null || id === undefined || id === '') throw new Error('Select a hotline to edit.');
  const payload = buildHotlinePayload(form);
  await requireHotlineWriteAccess(client);
  const result = await client.from('emergency_hotlines').update(payload).eq('id', id).select('id');
  return checkResult(result, 'edit');
}

export async function removeHotline(client, id) {
  if (id === null || id === undefined || id === '') throw new Error('Select a hotline to delete.');
  await requireHotlineWriteAccess(client);
  const result = await client.from('emergency_hotlines').delete().eq('id', id).select('id');
  return checkResult(result, 'delete');
}
