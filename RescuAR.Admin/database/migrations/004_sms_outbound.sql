-- Execute in Supabase SQL editor. The browser has NO direct access to the queue.
create table if not exists public.sms_outbox (
  id uuid primary key default gen_random_uuid(),
  recipient text not null check (recipient ~ '^\+639[0-9]{9}$'),
  message text not null check (char_length(message) between 1 and 160),
  status text not null default 'queued' check (status in ('queued','claimed','sent','failed','unknown')),
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now(),
  claimed_at timestamptz,
  completed_at timestamptz,
  device_id text,
  lease_token uuid,
  error_message text
);
create index if not exists sms_outbox_queue_idx on public.sms_outbox(status, created_at);
alter table public.sms_outbox enable row level security;
revoke all on public.sms_outbox from anon, authenticated;
-- This function is invoked using service_role by the edge function only.
create or replace function public.sms_claim_next(p_device_id text)
returns table(id uuid, recipient text, message text, lease_token uuid)
language plpgsql security definer set search_path = public, pg_temp as $$
declare v_id uuid; v_token uuid := gen_random_uuid();
begin
  select o.id into v_id from public.sms_outbox o where o.status = 'queued'
  order by o.created_at for update skip locked limit 1;
  if v_id is null then return; end if;
  return query update public.sms_outbox o set status='claimed', claimed_at=now(), device_id=p_device_id,
    lease_token=v_token where o.id=v_id returning o.id,o.recipient,o.message,o.lease_token;
end $$;
revoke all on function public.sms_claim_next(text) from public, anon, authenticated;
grant execute on function public.sms_claim_next(text) to service_role;
