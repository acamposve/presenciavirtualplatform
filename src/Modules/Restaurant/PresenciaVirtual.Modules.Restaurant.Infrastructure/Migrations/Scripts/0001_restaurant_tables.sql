-- Minimal reference schema for a restaurant table. Table Management (creation, editing,
-- floor plans, etc.) is out of scope of the CreateOrder specification; this is only enough
-- to let CreateOrder validate a table reference and to exercise tenant isolation (ADR 0002).

CREATE SCHEMA IF NOT EXISTS restaurant;

CREATE TABLE restaurant.tables
(
    id         uuid PRIMARY KEY,
    tenant_id  uuid NOT NULL,
    label      text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_restaurant_tables_tenant_id ON restaurant.tables (tenant_id);

ALTER TABLE restaurant.tables ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.tables FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON restaurant.tables
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
