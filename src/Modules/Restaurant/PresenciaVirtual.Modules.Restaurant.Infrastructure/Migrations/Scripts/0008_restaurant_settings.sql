-- Minimal per-tenant configuration reference, per specs/restaurant/ordering/add-item.md (BR7).
-- Full Restaurant Settings management (an API/UI to configure this) is out of scope; a tenant
-- with no row here is treated as having no alcoholic-item quantity limit.

CREATE TABLE restaurant.settings
(
    tenant_id                             uuid PRIMARY KEY,
    max_alcoholic_item_quantity_per_line  integer NULL
        CHECK (max_alcoholic_item_quantity_per_line IS NULL OR max_alcoholic_item_quantity_per_line > 0)
);

ALTER TABLE restaurant.settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.settings FORCE ROW LEVEL SECURITY;

-- See 0001_restaurant_tables.sql for why NULLIF is required here.
CREATE POLICY tenant_isolation ON restaurant.settings
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
