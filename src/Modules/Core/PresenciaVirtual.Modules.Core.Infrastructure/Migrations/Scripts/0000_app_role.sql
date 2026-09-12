-- Least-privilege runtime role for the application (ADR 0002). Migrations themselves run as
-- the admin/superuser role configured for the platform; the application's own connections
-- (NpgsqlTenantDbConnectionFactory) use this role instead, so that PostgreSQL Row-Level
-- Security actually applies — RLS is not enforced against superusers or roles with
-- BYPASSRLS, which the Postgres image's own POSTGRES_USER is granted by default.
--
-- The password is supplied at migration time via DbUp variable substitution
-- ($AppRolePassword$) rather than hardcoded here, so it is never committed to source control.

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'presenciavirtual_app') THEN
        CREATE ROLE presenciavirtual_app LOGIN PASSWORD '$AppRolePassword$'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    ELSE
        ALTER ROLE presenciavirtual_app WITH PASSWORD '$AppRolePassword$';
    END IF;
END
$$;
