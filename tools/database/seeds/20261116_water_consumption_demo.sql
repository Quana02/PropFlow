-- Local operational seed explicitly requested by the user on 2026-11-16.
-- Input: psql -v seed_json=<requests array from docs/testing/data/water-consumption-demo.json>.
-- Only the existing DEMO-1801 apartment is eligible. Never overwrites a reading or invoice.
-- This is a development import, not an application migration or startup seed.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SELECT pg_advisory_xact_lock(71241001);

CREATE TEMP TABLE water_demo_target ON COMMIT DROP AS
SELECT id FROM apartments.apartment_units WHERE unit_number = 'DEMO-1801' AND status = 'ACTIVE';
CREATE TEMP TABLE water_demo_actor ON COMMIT DROP AS
SELECT u.user_id AS id FROM administration.user_role_assignments u
JOIN administration.roles r ON r.id = u.role_id
JOIN auth.users a ON a.id = u.user_id
WHERE r.code = 'MANAGER' AND r.is_active AND a.status = 'ACTIVE';

CREATE TEMP TABLE water_demo_rows ON COMMIT DROP AS
SELECT md5('propflow:water-demo:v1:DEMO-1801:' || x."usageMonth")::uuid AS id,
    x."usageMonth"::date AS usage_month,
    x."currentElectricityReading" AS electricity,
    lag(x."currentWaterReading", 1, 0::numeric) OVER (ORDER BY x."usageMonth") AS previous_water_reading,
    x."currentWaterReading" AS current_water_reading
FROM jsonb_to_recordset(:'seed_json'::jsonb)
    AS x("usageMonth" text, "currentElectricityReading" numeric, "currentWaterReading" numeric);

DO $$
BEGIN
    IF current_database() <> 'propflow' THEN RAISE EXCEPTION 'This seed is restricted to the local propflow database'; END IF;
    IF (SELECT count(*) FROM water_demo_target) <> 1 THEN RAISE EXCEPTION 'Expected exactly one active DEMO-1801 apartment'; END IF;
    IF (SELECT count(*) FROM water_demo_actor) <> 1 THEN RAISE EXCEPTION 'Expected exactly one active Manager to own the demo records'; END IF;
    IF (SELECT count(*) FROM water_demo_rows) <> 10 OR (SELECT count(DISTINCT usage_month) FROM water_demo_rows) <> 10
        OR EXISTS (SELECT 1 FROM water_demo_rows WHERE usage_month NOT BETWEEN DATE '2026-01-01' AND DATE '2026-10-01'
            OR extract(day FROM usage_month) <> 1 OR electricity IS NOT NULL OR current_water_reading IS NULL
            OR current_water_reading < previous_water_reading OR current_water_reading > 1000000
            OR current_water_reading <> round(current_water_reading, 3))
        OR (SELECT current_water_reading FROM water_demo_rows WHERE usage_month = DATE '2026-01-01') <> 0
    THEN RAISE EXCEPTION 'The sample readings are invalid or incomplete'; END IF;
END $$;

-- Serialize with edits to the target apartment and refuse to mix sample data with existing readings.
SELECT id FROM apartments.apartment_units WHERE id = (SELECT id FROM water_demo_target) FOR UPDATE;
LOCK TABLE billing.electricity_usages IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE water_demo_preserve ON COMMIT DROP AS
SELECT
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(i) ORDER BY i.id)::text, '[]')) FROM billing.invoices i
        WHERE i.apartment_unit_id = (SELECT id FROM water_demo_target)) AS invoice_digest,
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY u.id)::text, '[]')) FROM billing.electricity_usages u
        WHERE u.apartment_id <> (SELECT id FROM water_demo_target)) AS other_readings_digest;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM billing.electricity_usages u LEFT JOIN water_demo_rows s ON s.usage_month = u.usage_month
        WHERE u.apartment_id = (SELECT id FROM water_demo_target)
          AND (s.id IS NULL OR u.id <> s.id OR u.kwh IS NOT NULL
            OR u.current_electricity_reading IS NOT NULL OR u.previous_electricity_reading IS NOT NULL
            OR u.current_water_reading IS DISTINCT FROM s.current_water_reading
            OR u.previous_water_reading IS DISTINCT FROM s.previous_water_reading
            OR u.water_m3 IS DISTINCT FROM s.current_water_reading - s.previous_water_reading)
    ) THEN RAISE EXCEPTION 'Existing apartment readings differ from this demo; no data was changed'; END IF;
    -- Mirror the Billing lock: issued/cancelled service-fee snapshots must not be changed by an import.
    IF EXISTS (
        SELECT 1 FROM water_demo_rows s JOIN billing.invoices i ON i.apartment_unit_id = (SELECT id FROM water_demo_target)
          AND i.billing_period_start = (s.usage_month + INTERVAL '1 month')::date
        WHERE (i.invoice_type = 'SERVICE_FEE' OR i.invoice_type IS NULL AND EXISTS (
            SELECT 1 FROM billing.invoice_items item WHERE item.invoice_id = i.id AND item.fee_code_snapshot <> 'APARTMENT_RENT'))
          AND NOT EXISTS (SELECT 1 FROM billing.electricity_usages u WHERE u.apartment_id = i.apartment_unit_id AND u.usage_month = s.usage_month)
    ) THEN RAISE EXCEPTION 'A service-fee invoice locks one of the missing sample periods'; END IF;
END $$;

CREATE TEMP TABLE water_demo_inserted ON COMMIT DROP AS
WITH inserted AS (
    INSERT INTO billing.electricity_usages
        (id, apartment_id, usage_month, kwh, water_m3, previous_electricity_reading, current_electricity_reading,
         previous_water_reading, current_water_reading, created_by, updated_by, created_at, updated_at)
    SELECT s.id, target.id, s.usage_month, NULL, s.current_water_reading - s.previous_water_reading, NULL, NULL,
        s.previous_water_reading, s.current_water_reading, actor.id, actor.id, now(), now()
    FROM water_demo_rows s CROSS JOIN water_demo_target target CROSS JOIN water_demo_actor actor
    ON CONFLICT (apartment_id, usage_month) DO NOTHING
    RETURNING *
) SELECT * FROM inserted;

-- Null audit actor denotes a local development operation, not a Manager clicking in the application.
-- created_by/updated_by on sample readings identify the existing demo Manager who owns the records.
INSERT INTO administration.audit_logs
    (id, actor_user_id, action, entity_type, entity_id, old_values, new_values, correlation_id, created_at)
SELECT md5('propflow:water-demo:audit:v1:' || s.id::text)::uuid, NULL, 'IMPORT_DEMO_WATER_READINGS', 'ElectricityUsage', s.id, NULL,
    jsonb_build_object('source', 'LOCAL_DEVELOPMENT_SEED', 'fixture', 'water-consumption-demo.json',
        'apartment', 'DEMO-1801', 'usageMonth', s.usage_month, 'previousWaterReading', s.previous_water_reading,
        'currentWaterReading', s.current_water_reading, 'waterM3', s.water_m3),
    'water-demo-v1:DEMO-1801', now()
FROM water_demo_inserted s;

DO $$
BEGIN
    IF (SELECT count(*) FROM billing.electricity_usages WHERE apartment_id = (SELECT id FROM water_demo_target)) <> 10
    THEN RAISE EXCEPTION 'Unexpected sample row count'; END IF;
    IF (SELECT invoice_digest FROM water_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(i) ORDER BY i.id)::text, '[]')) FROM billing.invoices i WHERE i.apartment_unit_id = (SELECT id FROM water_demo_target))
    THEN RAISE EXCEPTION 'Invoice data unexpectedly changed; import rolled back'; END IF;
    IF (SELECT other_readings_digest FROM water_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY u.id)::text, '[]')) FROM billing.electricity_usages u WHERE u.apartment_id <> (SELECT id FROM water_demo_target))
    THEN RAISE EXCEPTION 'Another apartment reading changed; import rolled back'; END IF;
END $$;

SELECT 'DEMO-1801' AS apartment, count(*) AS inserted_readings FROM water_demo_inserted;
COMMIT;
