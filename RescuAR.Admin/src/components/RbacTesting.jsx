import React,{useState} from 'react';
import {supabase} from '../supabaseClient';
const areas = level => level>=18 ? ['Tumana','Malanday','Nangka','Concepcion','Barangka','Tañong','Calumpang','Santo Niño','San Roque']
 : level>=17 ? ['Tumana','Malanday','Nangka','Concepcion','Barangka','Tañong']
 : level>=16 ? ['Tumana','Malanday','Nangka','Concepcion']
 : level>=15 ? ['Tumana','Malanday'] : [];
export default function RbacTesting(){
 const [level,setLevel]=useState(16),[subject,setSubject]=useState('TEST: Flood advisory'),
 [message,setMessage]=useState('Simulation only. No action required.'),[logs,setLogs]=useState([]),
 [status,setStatus]=useState('');
 const refresh=async()=>{const {data,error}=await supabase.rpc('rbac_list_tests');if(error)setStatus(error.message);else setLogs(data||[])};
 const save=async(kind,payload)=>{
  setStatus('');
  const {error}=await supabase.rpc('rbac_log_test',{p_kind:kind,p_payload:payload});
  setStatus(error?error.message:'Simulation result logged. Nothing was sent to residents or written to monitoring telemetry.');
  if(!error)await refresh();
 };
 React.useEffect(()=>{void refresh()},[]);
 const numeric=Number(level),affected=areas(numeric);
 const alarm=numeric>=18?'Level 3':numeric>=16?'Level 2':numeric>=15?'Level 1':'Below Level 1';
 return <section className="main-view" style={{padding:24,maxWidth:1000}}>
  <h1>Testing & Simulation</h1>
  <p style={{color:'#991b1b',fontWeight:700}}>TEST ONLY — isolated from live river data and public notifications.</p>
  <section style={{padding:18,background:'#fff',margin:'12px 0',borderRadius:8}}>
   <h2>River-Level Test</h2>
   <label>Simulated reading (meters): <input type="number" min="0" max="30" step=".1" value={level} onChange={e=>setLevel(e.target.value)} /></label>
   <p>Legacy PREP threshold: <strong>{alarm}</strong></p>
   <p>Possible barangays (unvalidated thresholds): {affected.join(', ')||'No rule triggered'}</p>
   <p>This is a threshold preview, not a hydraulic flood simulation or emergency warning.</p>
   <button disabled={!Number.isFinite(numeric)||numeric<0||numeric>30} onClick={()=>save('river_level',{meters:numeric,alarm,affected,mode:'simulation',warning:'legacy-unvalidated'})}>Save Test Record</button>
  </section>
  <section style={{padding:18,background:'#fff',margin:'12px 0',borderRadius:8}}>
   <h2>Advisory Preview</h2>
   <label>Title <input value={subject} onChange={e=>setSubject(e.target.value)} style={{width:'100%',padding:7}}/></label>
   <label>Message <textarea value={message} onChange={e=>setMessage(e.target.value)} style={{display:'block',width:'100%',minHeight:80}}/></label>
   <div style={{padding:10,border:'1px solid #ddd',margin:'8px 0'}}><strong>[TEST ONLY] {subject}</strong><p>{message}</p></div>
   <button disabled={!subject.trim()||!message.trim()} onClick={()=>save('advisory_preview',{subject:subject.slice(0,250),message:message.slice(0,2000),mode:'test_only'})}>Save Preview Record</button>
   <p>No SMS, push notification, or public advisory is sent.</p>
  </section>
  {status&&<p role="status">{status}</p>}
  <h2>Recent Test History</h2>
  <ul>{logs.map(e=><li key={e.id}>{new Date(e.created_at).toLocaleString()} — {e.kind} — {JSON.stringify(e.payload).slice(0,180)}</li>)}</ul>
 </section>
}
