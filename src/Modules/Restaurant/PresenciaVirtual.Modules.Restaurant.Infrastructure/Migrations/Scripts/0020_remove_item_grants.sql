-- Grants the least-privilege application role (presenciavirtual_app, 0000_app_role.sql) the
-- access it needs for RemoveItem.
--
-- DELETE on restaurant.order_items is the one genuinely new privilege: 0010_add_item_grants.sql
-- deliberately granted only SELECT, INSERT, UPDATE, because AddItem never deletes. RemoveItem is
-- the first capability in the module that removes a line, so without this grant the delete would
-- fail with InsufficientPrivilege rather than working at all.

GRANT DELETE ON restaurant.order_items TO presenciavirtual_app;
GRANT SELECT, INSERT ON restaurant.remove_item_idempotency_keys TO presenciavirtual_app;
