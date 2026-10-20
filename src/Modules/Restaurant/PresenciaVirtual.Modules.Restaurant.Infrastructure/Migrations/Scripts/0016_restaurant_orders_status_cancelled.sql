-- Ordering / CancelOrder (specs/restaurant/ordering/cancel-order.md BR6). Widens the CHECK
-- constraint 0011_restaurant_orders_status_closed.sql narrowed to ('Open', 'Closed') - without
-- this, any attempt to persist 'Cancelled' would be rejected outright.
--
-- The partial unique index ux_restaurant_orders_open_per_table (create-order.md BR2) is
-- deliberately untouched: it already applies only WHERE status = 'Open', which is exactly what
-- lets a cancelled order free its table for a new CreateOrder (cancel-order.md AC10) without
-- counting against the constraint.

ALTER TABLE restaurant.orders DROP CONSTRAINT orders_status_check;
ALTER TABLE restaurant.orders ADD CONSTRAINT orders_status_check CHECK (status IN ('Open', 'Closed', 'Cancelled'));
