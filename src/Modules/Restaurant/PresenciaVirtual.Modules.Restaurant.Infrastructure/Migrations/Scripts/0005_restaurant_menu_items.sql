-- Minimal reference schema for a menu item, per specs/restaurant/ordering/add-item.md.
-- Full Menu Management (creating/editing items, categories, availability) is out of scope;
-- this is only enough for AddItem to reference an existing menu item.

CREATE TABLE restaurant.menu_items
(
    id           uuid PRIMARY KEY,
    tenant_id    uuid NOT NULL,
    name         text NOT NULL,
    price        numeric(10, 2) NOT NULL,
    is_alcoholic boolean NOT NULL DEFAULT false,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_restaurant_menu_items_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE INDEX ix_restaurant_menu_items_tenant_id ON restaurant.menu_items (tenant_id);

ALTER TABLE restaurant.menu_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.menu_items FORCE ROW LEVEL SECURITY;

-- See 0001_restaurant_tables.sql for why NULLIF is required here.
CREATE POLICY tenant_isolation ON restaurant.menu_items
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
