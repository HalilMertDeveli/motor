-- MOTOR: dedicated application role for Smartstore on Supabase PostgreSQL.
--
-- Run once per Supabase project (staging and production are separate projects)
-- in the Supabase SQL editor, connected as "postgres". Safe to re-run.
--
-- Afterwards set the password in a separate statement that you never save or commit:
--   ALTER ROLE motor_app WITH PASSWORD '<generated, e.g. openssl rand -base64 32>';
-- Until then the role cannot log in.
--
-- Why a dedicated role:
-- * Supabase grants anon/authenticated (the Data API roles) access to tables that
--   "postgres" creates in schema public. Tables owned by motor_app do not get
--   those default grants, and the statements below revoke the rest.
-- * Smartstore runs its migrations at startup, so the role owns its tables and
--   needs CREATE on schema public, but it is not a superuser.

do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'motor_app') then
        create role motor_app with login noinherit nocreatedb nocreaterole;
    end if;
end
$$;

grant connect, temporary on database postgres to motor_app;
grant usage, create on schema public to motor_app;

-- Smartstore maps every string column to citext. citext is a trusted extension;
-- create it up front in Supabase's "extensions" schema so the app does not need
-- to, and keep "extensions" on the role's search path so the type resolves.
create schema if not exists extensions;
create extension if not exists citext with schema extensions;
grant usage on schema extensions to motor_app;

alter role motor_app set search_path = public, extensions;

-- Server-side safety nets. Smartstore's own command timeouts stay lower.
alter role motor_app set statement_timeout = '120s';
alter role motor_app set idle_in_transaction_session_timeout = '60s';
alter role motor_app set lock_timeout = '30s';

-- Keep the Supabase Data API (PostgREST: anon, authenticated) away from MOTOR data.
do $$
declare
    api_role text;
begin
    foreach api_role in array array['anon', 'authenticated']
    loop
        if exists (select 1 from pg_roles where rolname = api_role) then
            execute format('revoke all on all tables in schema public from %I', api_role);
            execute format('revoke all on all sequences in schema public from %I', api_role);
            execute format('revoke all on all functions in schema public from %I', api_role);
            execute format('revoke usage, create on schema public from %I', api_role);
            execute format('alter default privileges for role motor_app in schema public revoke all on tables from %I', api_role);
            execute format('alter default privileges for role motor_app in schema public revoke all on sequences from %I', api_role);
            execute format('alter default privileges for role motor_app in schema public revoke all on functions from %I', api_role);
        end if;
    end loop;
end
$$;

-- Functions are executable by PUBLIC by default; not for MOTOR's.
alter default privileges for role motor_app in schema public revoke execute on functions from public;
