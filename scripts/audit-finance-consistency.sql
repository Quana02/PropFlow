-- Read-only audit. No resident names, addresses, credentials or financial mutations.
BEGIN TRANSACTION READ ONLY;
SELECT invoice_type, count(*) AS invoices, sum(total_amount) AS total FROM billing.invoices GROUP BY invoice_type;
SELECT 'duplicate_invoice_keys' AS check_name, count(*) AS violations FROM (
  SELECT apartment_unit_id,billing_period_start,billing_period_end,invoice_type
  FROM billing.invoices GROUP BY 1,2,3,4 HAVING count(*) > 1
) duplicates;
SELECT 'orphan_payments' AS check_name,count(*) AS violations FROM payments.payments p LEFT JOIN billing.invoices i ON i.id=p.invoice_id WHERE i.id IS NULL;
SELECT 'overpaid_invoices' AS check_name,count(*) AS violations FROM (
  SELECT i.id FROM billing.invoices i JOIN payments.payments p ON p.invoice_id=i.id AND p.status='CONFIRMED'
  GROUP BY i.id,i.total_amount HAVING sum(p.amount)>i.total_amount
) excess;
SELECT 'item_total_mismatch' AS check_name,count(*) AS violations FROM billing.invoices i
WHERE i.total_amount <> COALESCE((SELECT sum(line_amount) FROM billing.invoice_items l WHERE l.invoice_id=i.id),0);
SELECT 'orphan_recipient_invoices' AS check_name,count(*) AS violations FROM billing.invoice_recipients r LEFT JOIN billing.invoices i ON i.id=r.invoice_id WHERE i.id IS NULL;
SELECT 'invoice_notification_wrong_recipient' AS check_name,count(*) AS violations FROM communication.notifications n
WHERE n.source_type='INVOICE' AND NOT EXISTS (SELECT 1 FROM billing.invoice_recipients r WHERE r.invoice_id=n.source_id AND r.user_id=n.recipient_user_id);
SELECT 'payment_notification_wrong_recipient' AS check_name,count(*) AS violations FROM communication.notifications n
WHERE n.source_type='PAYMENT' AND NOT EXISTS (SELECT 1 FROM payments.payments p JOIN billing.invoice_recipients r ON r.invoice_id=p.invoice_id WHERE p.id=n.source_id AND r.user_id=n.recipient_user_id);
SELECT 'duplicate_notification_events' AS check_name,count(*) AS violations FROM (
  SELECT recipient_user_id,source_event_id FROM communication.notifications WHERE source_event_id IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
) duplicates;
SELECT 'duplicate_payment_operation_keys' AS check_name,count(*) AS violations FROM (
  SELECT idempotency_key FROM payments.payment_operations GROUP BY idempotency_key HAVING count(*)>1
) duplicates;
SELECT status,count(*) AS payments,sum(amount) AS total FROM payments.payments GROUP BY status;
SELECT count(*) AS email_outbox,count(*) FILTER(WHERE sent_at IS NOT NULL) AS smtp_accepted,
 count(*) FILTER(WHERE lower(address) LIKE '%.example' OR lower(address) LIKE '%.invalid') AS reserved_demo,
 count(*) FILTER(WHERE lower(address) NOT LIKE '%.example' AND lower(address) NOT LIKE '%.invalid') AS external_addresses
FROM communication.finance_emails;
COMMIT;
