import React, { useCallback, useEffect, useState } from 'react';
import { supabase } from '../supabaseClient';
import './SmsOutboundConsole.css';

const endpoint = `${import.meta.env.VITE_SUPABASE_URL}/functions/v1/sms-gateway`;
async function request(action, extra={}) {
  const {data:{session}}=await supabase.auth.getSession();
  if (!session?.access_token) throw new Error('Sign in again to continue.');
  const response=await fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json',Authorization:`Bearer ${session.access_token}`,apikey:import.meta.env.VITE_SUPABASE_ANON_KEY},body:JSON.stringify({action,...extra})});
  const result=await response.json();
  if(!response.ok) throw new Error(result.error||'Request failed');
  return result;
}
const smsLimit = (text) => /^[\x00-\x7F]*$/.test(text) ? 160 : 70; // Conservative single-segment limit; actual GSM-7 extensions may consume two units.
const parseNumbers = (s) => [...new Set(s.split(/[\s,;]+/).map(n=>n.trim()).filter(Boolean))];
export default function SmsOutboundConsole(){
  const [numbers,setNumbers]=useState(''); const [message,setMessage]=useState('');
  const [items,setItems]=useState([]); const [registered,setRegistered]=useState([]); const [busy,setBusy]=useState(false); const [error,setError]=useState(''); const [notice,setNotice]=useState('');
  const refresh=useCallback(async()=>{try{const result=await request('list');setItems(result.messages||[]);setError('');}catch(e){setError(e.message);}},[]);
  useEffect(()=>{refresh();supabase.rpc('get_all_users_with_roles').then(({data,error})=>{
    if(!error) setRegistered((data||[]).filter(u=>(u.status||'active').toLowerCase()==='active' && u.phone_number));
  });},[refresh]);
  const recipients=parseNumbers(numbers);
  const maxSmsChars=smsLimit(message);
  const send=async e=>{e.preventDefault();setError('');setNotice('');
    if(message.length > maxSmsChars){setError(`SMS exceeds the ${maxSmsChars}-character single-segment limit. Shorten your message.`);return;}
    if(!window.confirm(`Queue an SMS to ${recipients.length} recipient(s)? This may incur SIM charges.\n\n${message}`)) return;
    setBusy(true);try{const result=await request('enqueue',{recipients,message,confirm:true});setNotice(`${result.queued} SMS message(s) queued. They have NOT been delivered yet.`);setNumbers('');setMessage('');await refresh();}catch(err){setError(err.message);}finally{setBusy(false);}
  };
  return <section className="sms-outbound">
    <header><h2>Outbound SMS Gateway</h2><p>Send SMS through an authorized RescuAR Android SIM gateway. Queued messages are not delivery confirmations.</p></header>
    <form onSubmit={send} className="sms-outbound-panel">
      <label htmlFor="sms-recipients">Recipient mobile numbers (Philippines)</label>
      <textarea id="sms-recipients" value={numbers} onChange={e=>setNumbers(e.target.value)} required placeholder="09XXXXXXXXX (comma, space or newline separated)" rows={3}/>
      <small>{recipients.length} unique recipient(s); maximum 100 per submission. Only registered active users are accepted by the server.</small>
      <label htmlFor="sms-registered">Add a registered user</label>
      <select id="sms-registered" defaultValue="" onChange={e=>{if(e.target.value){setNumbers(v=>v ? v+'\n'+e.target.value : e.target.value);e.target.value='';}}}>
        <option value="">Choose registered user…</option>{registered.map(u=><option key={u.id} value={u.phone_number}>{[u.first_name,u.last_name].filter(Boolean).join(' ')||u.username||'User'} — {u.phone_number}</option>)}
      </select>
      <label htmlFor="sms-body">SMS message</label>
      <textarea id="sms-body" value={message} onChange={e=>setMessage(e.target.value)} required maxLength={160} rows={5} placeholder="RESCUAR: Official advisory…" />
      <small>{message.length}/{maxSmsChars} conservative single-segment characters. Avoid emoji and special symbols.</small>
      <button disabled={busy||!recipients.length||!message.trim()||recipients.length>100||message.length>maxSmsChars} type="submit">{busy?'Queuing…':'Review and queue SMS'}</button>
    </form>
    {error&&<p role="alert" className="sms-outbound-error">{error}</p>}{notice&&<p role="status">{notice}</p>}
    <section className="sms-outbound-panel"><div className="sms-outbound-heading"><h3>Recent messages</h3><button type="button" onClick={refresh}>Refresh</button></div>
      <div className="sms-outbound-table"><table><thead><tr><th>Date</th><th>Recipient</th><th>Message</th><th>Status</th><th>Failure reason</th></tr></thead><tbody>
      {items.map(i=><tr key={i.id}><td>{new Date(i.created_at).toLocaleString()}</td><td>{i.recipient}</td><td>{i.message}</td><td>{i.status}</td><td>{i.error_message||'—'}</td></tr>)}
      {!items.length&&<tr><td colSpan="5">No SMS messages queued yet.</td></tr>}</tbody></table></div>
    </section>
  </section>;
}
