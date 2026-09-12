-- Required so restaurant.order_items can use a tenant-consistent composite foreign key,
-- (tenant_id, order_id) -> restaurant.orders (tenant_id, id), per
-- specs/restaurant/ordering/add-item.md's Data Requirements (a row can never reference an
-- order belonging to a different tenant than its own tenant_id).

ALTER TABLE restaurant.orders
    ADD CONSTRAINT ux_restaurant_orders_tenant_id_id UNIQUE (tenant_id, id);
