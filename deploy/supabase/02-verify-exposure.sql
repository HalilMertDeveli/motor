-- MOTOR: checks that the Supabase Data API roles cannot reach MOTOR data.
-- Run in the Supabase SQL editor after installation and after every upgrade.
-- Every row must report ok = true.
--
-- Effective privileges are checked (has_*_privilege), so grants through PUBLIC
-- count too. Schema USAGE alone exposes no rows and is not checked.

with api_roles as (
    select rolname from pg_roles where rolname in ('anon', 'authenticated')
)
select 'no table access in public' as check_name,
       r.rolname as role,
       not exists (
           select 1
           from pg_class c
           join pg_namespace n on n.oid = c.relnamespace
           where n.nspname = 'public'
             and c.relkind in ('r', 'p', 'v', 'm', 'f')
             and has_table_privilege(r.rolname, c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
       ) as ok
from api_roles r

union all

select 'no sequence access in public',
       r.rolname,
       not exists (
           select 1
           from pg_class c
           join pg_namespace n on n.oid = c.relnamespace
           where n.nspname = 'public'
             -- CASE keeps has_sequence_privilege away from non-sequences
             and case when c.relkind = 'S' then has_sequence_privilege(r.rolname, c.oid, 'USAGE, SELECT, UPDATE') else false end
       )
from api_roles r

union all

select 'no function execute in public (motor_app owned)',
       r.rolname,
       not exists (
           select 1
           from pg_proc p
           join pg_namespace n on n.oid = p.pronamespace
           where n.nspname = 'public'
             and pg_get_userbyid(p.proowner) = 'motor_app'
             and has_function_privilege(r.rolname, p.oid, 'EXECUTE')
       )
from api_roles r

union all

select 'motor_app is not privileged',
       'motor_app',
       not rolsuper and not rolcreaterole and not rolcreatedb and not rolbypassrls
from pg_roles
where rolname = 'motor_app'

order by 1, 2;
