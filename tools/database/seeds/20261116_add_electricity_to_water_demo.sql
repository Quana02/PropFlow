-- Local demo extension explicitly requested on 2026-11-16, after the water-only import.
-- Input: requests array from docs/testing/data/utility-consumption-demo.json as psql seed_json.
-- Adds electricity only to the known water seed. No bills, notifications or other apartment data.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SELECT pg_advisory_xact_lock(71241001);
CREATE TEMP TABLE utility_demo_target ON COMMIT DROP AS
SELECT id FROM apartments.apartment_units WHERE unit_number='DEMO-1801' AND status='ACTIVE';
CREATE TEMP TABLE utility_demo_actor ON COMMIT DROP AS
SELECT u.user_id AS id FROM administration.user_role_assignments u
JOIN administration.roles r ON r.id=u.role_id JOIN auth.users a ON a.id=u.user_id
WHERE r.code='MANAGER' AND r.is_active AND a.status='ACTIVE';
CREATE TEMP TABLE utility_demo_rows ON COMMIT DROP AS
SELECT md5('propflow:water-demo:v1:DEMO-1801:' || x."usageMonth")::uuid AS id, x."usageMonth"::date AS usage_month,
    lag(x."currentElectricityReading",1,0::numeric) OVER (ORDER BY x."usageMonth") AS previous_electricity_reading,
    x."currentElectricityReading" AS current_electricity_reading,
    lag(x."currentWaterReading",1,0::numeric) OVER (ORDER BY x."usageMonth") AS previous_water_reading,
    x."currentWaterReading" AS current_water_reading
FROM jsonb_to_recordset(:'seed_json'::jsonb) AS x("usageMonth" text,"currentElectricityReading" numeric,"currentWaterReading" numeric);
DO $$
BEGIN
    IF current_database()<>'propflow' OR (SELECT count(*) FROM utility_demo_target)<>1 OR (SELECT count(*) FROM utility_demo_actor)<>1
    THEN RAISE EXCEPTION 'Expected local propflow, active DEMO-1801 and exactly one active Manager'; END IF;
    IF (SELECT count(*) FROM utility_demo_rows)<>10 OR (SELECT count(DISTINCT usage_month) FROM utility_demo_rows)<>10
       OR EXISTS(SELECT 1 FROM utility_demo_rows WHERE usage_month NOT BETWEEN DATE '2026-01-01' AND DATE '2026-10-01'
          OR extract(day FROM usage_month)<>1 OR current_electricity_reading IS NULL OR current_water_reading IS NULL
          OR current_electricity_reading < previous_electricity_reading OR current_electricity_reading > 1000000
          OR current_electricity_reading<>round(current_electricity_reading,3)
          OR current_water_reading < previous_water_reading OR current_water_reading > 1000000
          OR current_water_reading<>round(current_water_reading,3))
       OR EXISTS(SELECT 1 FROM utility_demo_rows WHERE usage_month=DATE '2026-01-01' AND (current_water_reading<>0 OR current_electricity_reading<>0))
    THEN RAISE EXCEPTION 'Invalid sample meter readings'; END IF;
END $$;
SELECT id FROM apartments.apartment_units WHERE id=(SELECT id FROM utility_demo_target) FOR UPDATE;
LOCK TABLE billing.electricity_usages IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE utility_demo_preserve ON COMMIT DROP AS
SELECT
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(i) ORDER BY i.id)::text,'[]')) FROM billing.invoices i WHERE i.apartment_unit_id=(SELECT id FROM utility_demo_target)) AS invoice_digest,
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY u.id)::text,'[]')) FROM billing.electricity_usages u WHERE u.apartment_id<>(SELECT id FROM utility_demo_target)) AS others_digest,
    (SELECT md5(coalesce(jsonb_agg(jsonb_build_array(u.id,u.usage_month,u.previous_water_reading,u.current_water_reading,u.water_m3,u.created_by,u.created_at) ORDER BY u.id)::text,'[]'))
        FROM billing.electricity_usages u WHERE u.apartment_id=(SELECT id FROM utility_demo_target)) AS water_digest;
DO $$
BEGIN
    IF (SELECT count(*) FROM billing.electricity_usages WHERE apartment_id=(SELECT id FROM utility_demo_target))<>10
       OR EXISTS(SELECT 1 FROM utility_demo_rows s LEFT JOIN billing.electricity_usages u ON u.id=s.id AND u.apartment_id=(SELECT id FROM utility_demo_target)
           WHERE u.id IS NULL OR u.usage_month<>s.usage_month OR u.current_water_reading IS DISTINCT FROM s.current_water_reading
              OR u.previous_water_reading IS DISTINCT FROM s.previous_water_reading OR u.water_m3 IS DISTINCT FROM s.current_water_reading-s.previous_water_reading
              OR NOT ((u.kwh IS NULL AND u.previous_electricity_reading IS NULL AND u.current_electricity_reading IS NULL)
                  OR (u.kwh IS NOT DISTINCT FROM s.current_electricity_reading-s.previous_electricity_reading
                      AND u.previous_electricity_reading IS NOT DISTINCT FROM s.previous_electricity_reading
                      AND u.current_electricity_reading IS NOT DISTINCT FROM s.current_electricity_reading)))
    THEN RAISE EXCEPTION 'Existing data differs from the known demo; no readings were changed'; END IF;
    IF EXISTS(SELECT 1 FROM utility_demo_rows s JOIN billing.electricity_usages u ON u.id=s.id AND u.kwh IS NULL
        JOIN billing.invoices i ON i.apartment_unit_id=u.apartment_id AND i.billing_period_start=(s.usage_month+INTERVAL '1 month')::date
        WHERE i.invoice_type='SERVICE_FEE' OR i.invoice_type IS NULL AND EXISTS(
            SELECT 1 FROM billing.invoice_items item WHERE item.invoice_id=i.id AND item.fee_code_snapshot<>'APARTMENT_RENT'))
    THEN RAISE EXCEPTION 'A service-fee invoice locks an electricity period'; END IF;
END $$;
CREATE TEMP TABLE utility_demo_changed ON COMMIT DROP AS
WITH changed AS (
    UPDATE billing.electricity_usages u SET
        kwh=s.current_electricity_reading-s.previous_electricity_reading,
        previous_electricity_reading=s.previous_electricity_reading,
        current_electricity_reading=s.current_electricity_reading,
        updated_by=actor.id, updated_at=now()
    FROM utility_demo_rows s CROSS JOIN utility_demo_actor actor
    WHERE u.id=s.id AND u.apartment_id=(SELECT id FROM utility_demo_target) AND u.kwh IS NULL
    RETURNING u.*
) SELECT * FROM changed;
INSERT INTO administration.audit_logs
    (id,actor_user_id,action,entity_type,entity_id,old_values,new_values,correlation_id,created_at)
SELECT md5('propflow:electricity-demo:audit:v1:'||u.id::text)::uuid,NULL,'IMPORT_DEMO_ELECTRICITY_READINGS','ElectricityUsage',u.id,
    jsonb_build_object('kwh',NULL,'previousElectricityReading',NULL,'currentElectricityReading',NULL),
    jsonb_build_object('source','LOCAL_DEVELOPMENT_SEED','fixture','utility-consumption-demo.json','usageMonth',u.usage_month,
        'previousElectricityReading',u.previous_electricity_reading,'currentElectricityReading',u.current_electricity_reading,'kwh',u.kwh),
    'electricity-demo-v1:DEMO-1801',now() FROM utility_demo_changed u;
DO $$
BEGIN
    IF (SELECT count(*) FROM billing.electricity_usages WHERE apartment_id=(SELECT id FROM utility_demo_target) AND kwh IS NOT NULL)<>10
    THEN RAISE EXCEPTION 'Expected ten complete electricity readings'; END IF;
    IF (SELECT water_digest FROM utility_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(jsonb_build_array(u.id,u.usage_month,u.previous_water_reading,u.current_water_reading,u.water_m3,u.created_by,u.created_at) ORDER BY u.id)::text,'[]')) FROM billing.electricity_usages u WHERE u.apartment_id=(SELECT id FROM utility_demo_target))
    THEN RAISE EXCEPTION 'Water data changed unexpectedly; import rolled back'; END IF;
    IF (SELECT invoice_digest FROM utility_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(i) ORDER BY i.id)::text,'[]')) FROM billing.invoices i WHERE i.apartment_unit_id=(SELECT id FROM utility_demo_target))
    THEN RAISE EXCEPTION 'Invoices changed unexpectedly; import rolled back'; END IF;
    IF (SELECT others_digest FROM utility_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY u.id)::text,'[]')) FROM billing.electricity_usages u WHERE u.apartment_id<>(SELECT id FROM utility_demo_target))
    THEN RAISE EXCEPTION 'Another apartment changed; import rolled back'; END IF;
END $$;
SELECT 'DEMO-1801' AS apartment,count(*) AS electricity_readings_added FROM utility_demo_changed;
COMMIT;
