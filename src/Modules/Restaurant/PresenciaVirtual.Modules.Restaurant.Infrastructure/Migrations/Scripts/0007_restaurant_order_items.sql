-- Ordering / AddItem (specs/restaurant/ordering/add-item.md).

CREATE TABLE restaurant.order_items
(
    id                  uuid PRIMARY KEY,
    tenant_id           uuid NOT NULL,
    order_id            uuid NOT NULL,
    menu_item_id        uuid NOT NULL,
    quantity            integer NOT NULL CHECK (quantity > 0),
    unit_price_snapshot numeric(10, 2) NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),

    -- BR4: at most one line per menu item per order. Also the ON CONFLICT target for the
    -- atomic merge in OrderItemRepository.AddOrMergeAsync.
    CONSTRAINT ux_restaurant_order_items_order_menu_item UNIQUE (tenant_id, order_id, menu_item_id),

    -- Tenant-consistent composite foreign keys: a row can never reference an order or menu
    -- item belonging to a different tenant than its own tenant_id, even if application code
    -- has a bug resolving those IDs (see add-item.md's Data Requirements).
    CONSTRAINT fk_restaurant_order_items_order
        FOREIGN KEY (tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id),
    CONSTRAINT fk_restaurant_order_items_menu_item
        FOREIGN KEY (tenant_id, menu_item_id) REFERENCES restaurant.menu_items (tenant_id, id)
);

CREATE INDEX ix_restaurant_order_items_tenant_id ON restaurant.order_items (tenant_id);

ALTER TABLE restaurant.order_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.order_items FORCE ROW LEVEL SECURITY;

-- See 0001_restaurant_tables.sql for why NULLIF is required here.
CREATE POLICY tenant_isolation ON restaurant.order_items
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
