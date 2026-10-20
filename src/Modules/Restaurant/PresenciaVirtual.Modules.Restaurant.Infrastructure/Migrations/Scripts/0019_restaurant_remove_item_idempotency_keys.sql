-- BR6: records which OrderId and MenuItemId a given Idempotency-Key applied, per tenant, for
-- RemoveItem specifically. Kept separate from CreateOrder's, AddItem's, CloseOrder's and
-- CancelOrder's own idempotency tables (each capability claims a different request shape and is
-- scoped independently per BR6's (TenantId, IdempotencyKey) rule) - a table shared across
-- capabilities would let a key reused across them collide, since nothing would distinguish which
-- operation it belongs to.

CREATE TABLE restaurant.remove_item_idempotency_keys
(
    tenant_id       uuid NOT NULL,
    idempotency_key text NOT NULL,
    order_id        uuid NOT NULL,
    menu_item_id    uuid NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, idempotency_key),

    -- Tenant-consistent composite foreign keys: a row can never reference an order or menu item
    -- belonging to a different tenant than its own tenant_id, even if application code has a bug
    -- resolving those identifiers (see remove-item.md's Data Requirements, mirroring
    -- order_items in 0007_restaurant_order_items.sql).
    CONSTRAINT fk_restaurant_remove_item_idempotency_keys_order
        FOREIGN KEY (tenant_id, order_id) REFERENCES restaurant.orders (tenant_id, id),
    CONSTRAINT fk_restaurant_remove_item_idempotency_keys_menu_item
        FOREIGN KEY (tenant_id, menu_item_id) REFERENCES restaurant.menu_items (tenant_id, id)
);

ALTER TABLE restaurant.remove_item_idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.remove_item_idempotency_keys FORCE ROW LEVEL SECURITY;

-- See 0001_restaurant_tables.sql for why NULLIF is required here.
CREATE POLICY tenant_isolation ON restaurant.remove_item_idempotency_keys
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
