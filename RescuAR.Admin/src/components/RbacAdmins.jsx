import React,{useEffect,useState} from 'react';
import {supabase} from '../supabaseClient';
const ROLES = [
 ['superadmin','Superadmin'],['research_head','Research Head'],
 ['operations_admin','Operations Admin'],['community_moderator','Community Moderator'],
 ['data_analyst','Data Analyst']
];
const style={padding:24,maxWidth:1000};
export default function RbacAdmins(){
 const [roles,setRoles]=useState(ROLES),[customName,setCustomName]=useState(''),[customLabel,setCustomLabel]=useState(''),[customPermissions,setCustomPermissions]=useState([]);
 const [rows,setRows]=useState([]),[email,setEmail]=useState(''),[role,setRole]=useState('data_analyst');
 const [error,setError]=useState(''),[busy,setBusy]=useState(false);
 const refresh=async()=>{
  const {data,error}=await supabase.rpc('rbac_list_accounts');
  if(error){setError(error.message);return;}
  setRows(data||[]);
  const {data:roleRows,error:roleError}=await supabase.rpc('rbac_list_roles');
  if(!roleError && roleRows) setRoles(roleRows.map(x=>[x.id,x.label]));
 };
 useEffect(()=>{void refresh()},[]);
 const change=async(enabled,targetEmail=email,targetRole=role)=>{
  if(!targetEmail.trim()) return setError('An existing Supabase Auth email is required.');
  setBusy(true);setError('');
  const {error}=await supabase.rpc('rbac_set_assignment',{p_email:targetEmail.trim(),p_role:targetRole,p_enabled:enabled});
  setBusy(false);
  if(error)setError(error.message);
  else {setEmail('');await refresh();}
 };
 return <section className="main-view" style={style}>
  <h1>Administrator Management</h1>
  <p>Assign roles to accounts already created in Supabase Authentication. No administrator can create Auth accounts from this browser.</p>
  {error && <p role="alert" style={{color:'#b91c1c'}}>{error}</p>}
  <div style={{display:'flex',flexWrap:'wrap',gap:10,margin:'15px 0'}}>
   <input aria-label="Existing administrator email" type="email" placeholder="Existing Auth user email" value={email}
    onChange={e=>setEmail(e.target.value)} style={{padding:9,minWidth:280}}/>
   <select aria-label="Role" value={role} onChange={e=>setRole(e.target.value)}>{roles.map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
   <button disabled={busy} onClick={()=>change(true)}>Assign Role</button>
   <button disabled={busy} onClick={refresh}>Refresh</button>
  </div>
  <details style={{margin:'16px 0',padding:14,background:'#fff'}}>
   <summary>Create Custom Role</summary>
   <p>Custom role IDs must begin with <code>custom_</code>. Superadmin permissions cannot be delegated here.</p>
   <input placeholder="custom_barangay_coordinator" value={customName} onChange={e=>setCustomName(e.target.value)} style={{margin:6,padding:8}}/>
   <input placeholder="Display label" value={customLabel} onChange={e=>setCustomLabel(e.target.value)} style={{margin:6,padding:8}}/>
   <div style={{display:'flex',flexWrap:'wrap',gap:12,margin:'10px 0'}}>
    {['dashboard.view','monitoring.view','hazard.view','research.view','research.manage','analytics.view','advisory.manage','evacuation.manage','hotline.manage','reports.moderate','residents.manage'].map(p=><label key={p}>
     <input type="checkbox" checked={customPermissions.includes(p)} onChange={e=>setCustomPermissions(prev=>e.target.checked?[...prev,p]:prev.filter(x=>x!==p))} /> {p}
    </label>)}
   </div>
   <button disabled={busy} onClick={async()=>{
     setBusy(true);setError('');
     const {error}=await supabase.rpc('rbac_create_custom_role',{p_id:customName,p_label:customLabel,p_permissions:customPermissions});
     setBusy(false);
     if(error)setError(error.message);else {setCustomName('');setCustomLabel('');setCustomPermissions([]);await refresh();}
   }}>Create Role</button>
  </details>
  <table style={{width:'100%',borderCollapse:'collapse'}}>
   <thead><tr><th style={{textAlign:'left'}}>Auth account</th><th style={{textAlign:'left'}}>Role</th><th style={{textAlign:'left'}}>Assigned</th><th>Action</th></tr></thead>
   <tbody>{rows.map(row=><tr key={`${row.user_id}:${row.role_id}`}>
    <td style={{padding:10}}>{row.email}</td><td>{row.role_id}</td>
    <td>{new Date(row.assigned_at).toLocaleString()}</td>
    <td><button disabled={busy} onClick={()=>{if(window.confirm(`Revoke ${row.role_id} from ${row.email}?`)) void change(false,row.email,row.role_id)}}>Revoke</button></td>
   </tr>)}</tbody>
  </table>
  <p style={{marginTop:20,color:'#64748b'}}>Superadmin reassignment is audited; self-removal and removal of the last Superadmin are blocked in the database.</p>
 </section>
}
