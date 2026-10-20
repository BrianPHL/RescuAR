// Deploy with JWT verification disabled: this function verifies user tokens explicitly
// and authenticates the phone separately with X-Gateway-Key. Set SMS_GATEWAY_KEY secret.
import { createClient } from 'https://esm.sh/@supabase/supabase-js@2';
const url = Deno.env.get('SUPABASE_URL')!;
const serviceKey = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!;
const gatewayKey = Deno.env.get('SMS_GATEWAY_KEY')!;
const db = createClient(url, serviceKey, { auth: { persistSession: false } });
const cors = { 'Access-Control-Allow-Origin': Deno.env.get('SMS_ADMIN_ORIGIN') || '',
  'Access-Control-Allow-Headers':'authorization, apikey, content-type, x-client-info',
  'Access-Control-Allow-Methods':'POST, OPTIONS' };
const json = (o: unknown, status = 200) => new Response(JSON.stringify(o), {status, headers:{...cors,'Content-Type':'application/json'}});
const normalize = (v: unknown) => {
  if (typeof v !== 'string') return null;
  const digits=v.replace(/[\s()-]/g,'');
  const number=digits.startsWith('09') && digits.length===11 ? '+63'+digits.slice(1)
    : digits.startsWith('639') && digits.length===12 ? '+'+digits
    : digits.startsWith('+639') && digits.length===13 ? digits : null;
  return number && /^\+639\d{9}$/.test(number) ? number : null;
};
const equal = (a:string,b:string) => { const enc=new TextEncoder(); const x=enc.encode(a),y=enc.encode(b); let d=x.length^y.length; for(let i=0;i<Math.max(x.length,y.length);i++) d|=(x[i]||0)^(y[i]||0); return d===0; };
Deno.serve(async req => {
  if(req.method==='OPTIONS') return new Response(null,{headers:cors});
  if(req.method!=='POST') return json({error:'Method not allowed'},405);
  try {
    const body=await req.json();
    const action=body.action;
    if(['claim','report'].includes(action)) {
      if (!gatewayKey || !equal(req.headers.get('X-Gateway-Key')||'',gatewayKey)) return json({error:'Unauthorized device'},401);
      const device=typeof body.device_id==='string' ? body.device_id : '';
      if(!/^[a-zA-Z0-9_-]{3,64}$/.test(device)) return json({error:'Invalid device'},400);
      if(action==='claim') {
        const {data,error}=await db.rpc('sms_claim_next',{p_device_id:device});
        if(error) throw error;
        return json({job:data?.[0]||null});
      }
      if(!['sent','failed','unknown'].includes(body.status) || typeof body.id!=='string' || typeof body.lease_token!=='string') return json({error:'Invalid report'},400);
      const {data,error}=await db.from('sms_outbox').update({status:body.status, completed_at:new Date().toISOString(),error_message:String(body.error||'').slice(0,300)})
        .eq('id',body.id).eq('device_id',device).eq('lease_token',body.lease_token).eq('status','claimed').select('id');
      if(error) throw error;
      return json({updated:(data||[]).length===1});
    }
    const authorization=req.headers.get('Authorization')||'';
    if(!authorization.startsWith('Bearer ')) return json({error:'Sign in required'},401);
    const {data:{user},error:authError}=await db.auth.getUser(authorization.slice(7));
    if(authError||!user) return json({error:'Invalid session'},401);
    const {data:admin,error:roleError}=await db.from('admin_roles').select('user_id').eq('user_id',user.id).maybeSingle();
    if(roleError||!admin) return json({error:'Admin required'},403);
    if(action==='list') {
      const {data,error}=await db.from('sms_outbox').select('id,recipient,message,status,created_at,completed_at,error_message').order('created_at',{ascending:false}).limit(100);
      if(error) throw error;
      return json({messages:data});
    }
    if(action==='enqueue') {
      const message=typeof body.message==='string'?body.message.trim():'';
      const raw=Array.isArray(body.recipients)?body.recipients:[];
      const limit=/^[\x00-\x7F]*$/.test(message) ? 160 : 70;
      if(message.length<1||message.length>limit||raw.length<1||raw.length>100) return json({error:`Message must fit one SMS segment (max ${limit} conservative characters) with 1–100 recipients`},400);
      const recipients=[...new Set(raw.map(normalize))];
      if(recipients.includes(null)) return json({error:'Invalid Philippine mobile number'},400);
      if(body.confirm!==true) return json({error:'Confirmation required'},400);
      // Only enrolled, active app users may receive outgoing SMS.
      const {data:registered,error:usersError}=await db.rpc('get_all_users_with_roles');
      if(usersError) return json({error:'Cannot verify registered recipients'},503);
      const activeNumbers=new Set((registered||[]).filter((u:{status?:string})=> (u.status||'active').toLowerCase()==='active')
        .map((u:{phone_number?:string})=>normalize(u.phone_number)).filter(Boolean));
      if(recipients.some(n=>!activeNumbers.has(n))) return json({error:'One or more numbers are not registered active RescuAR users'},400);
      const {data,error}=await db.from('sms_outbox').insert(recipients.map(recipient=>({recipient,message,created_by:user.id}))).select('id');
      if(error) throw error;
      return json({queued:data?.length||0});
    }
    return json({error:'Unknown action'},400);
  } catch(e) { console.error(e); return json({error:'Gateway request failed'},500); }
});
