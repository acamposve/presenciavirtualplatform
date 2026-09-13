-- Ordering / CloseOrder (specs/restaurant/ordering/close-order.md). Widens the CHECK constraint
-- 0002_restaurant_orders.sql's own comment anticipated widening once a second status value
-- existed. The partial unique index ux_restaurant_orders_open_per_table (BR2) is untouched: it
-- already only applies WHERE status = 'Open', so a Closed order never counts against it.

ALTER TABLE restaurant.orders DROP CONSTRAINT orders_status_check;
ALTER TABLE restaurant.orders ADD CONSTRAINT orders_status_check CHECK (status IN ('Open', 'Closed'));
