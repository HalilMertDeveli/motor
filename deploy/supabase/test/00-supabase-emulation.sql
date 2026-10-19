-- Test only: approximates the roles and default grants of a fresh Supabase
-- project on a plain postgres image, so 01-motor-app-role.sql and the
-- Smartstore installation can be verified without a real Supabase project.
-- Not a complete Supabase replica (no PostgREST, no pooler, no RLS policies).

create role anon nologin noinherit;
create role authenticated nologin noinherit;
create role service_role nologin noinherit bypassrls;

create schema if not exists extensions;
grant usage on schema extensions to anon, authenticated, service_role;

grant usage on schema public to anon, authenticated, service_role;
alter default privileges for role postgres in schema public grant all on tables to anon, authenticated, service_role;
alter default privileges for role postgres in schema public grant all on sequences to anon, authenticated, service_role;
alter default privileges for role postgres in schema public grant all on functions to anon, authenticated, service_role;

-- A table created by "postgres" the way a dashboard user would, to prove the revokes reach it.
create table public.preexisting_dashboard_table (id int primary key);
