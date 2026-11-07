-- Operational local-data setup, explicitly requested on 2026-11-02.
-- Research and rationale: docs/requirements/FE-03-Apartment-Type-Defaults.md.
-- Defaults are representative editable presets, not measured unit areas or market averages.
-- Intentionally NOT a schema migration, application startup seed, or fixed catalog.
BEGIN;
LOCK TABLE apartments.apartment_unit_types IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE apartment_type_setup_guard ON COMMIT DROP AS
SELECT
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY id)::text, '[]'))
        FROM apartments.apartment_units u) AS apartment_digest,
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(r) ORDER BY id)::text, '[]'))
        FROM billing.fee_rate_rules r) AS pricing_digest;

-- Reuse the known unused placeholder without deleting it or changing any referenced type.
UPDATE apartments.apartment_unit_types t
SET name = 'Căn hộ Studio', updated_at = now()
WHERE t.id = 'dd755a0b-b63c-47f8-ab42-8d983c1b5445'::uuid
  AND t.name = 'ok'
  AND NOT EXISTS (SELECT 1 FROM apartments.apartment_units u WHERE u.apartment_unit_type_id = t.id)
  AND NOT EXISTS (SELECT 1 FROM billing.fee_rate_rules r WHERE r.apartment_type_id = t.id)
  AND NOT EXISTS (SELECT 1 FROM apartments.apartment_unit_types other WHERE lower(btrim(other.name)) = lower('Căn hộ Studio'));

INSERT INTO apartments.apartment_unit_types
    (id, name, default_usable_area_m2, default_bedroom_count, default_bathroom_count, created_at, updated_at)
SELECT md5('propflow:commercial-apartment-type:' || preset.key)::uuid,
       preset.name, preset.area, preset.bedrooms, preset.bathrooms, now(), now()
FROM (VALUES
    ('studio', 'Căn hộ Studio', 35.00::numeric, 0, 1),
    ('one-bedroom', 'Căn hộ 1 phòng ngủ', 45.00::numeric, 1, 1),
    ('one-bedroom-plus', 'Căn hộ 1 phòng ngủ +1', 50.00::numeric, 1, 1),
    ('two-bedroom', 'Căn hộ 2 phòng ngủ', 75.00::numeric, 2, 2),
    ('three-bedroom', 'Căn hộ 3 phòng ngủ', 95.00::numeric, 3, 2)
) AS preset(key, name, area, bedrooms, bathrooms)
WHERE true
ON CONFLICT ((lower(btrim(name)))) DO UPDATE
SET default_usable_area_m2 = EXCLUDED.default_usable_area_m2,
    default_bedroom_count = EXCLUDED.default_bedroom_count,
    default_bathroom_count = EXCLUDED.default_bathroom_count,
    updated_at = now()
WHERE (apartment_unit_types.default_usable_area_m2,
       apartment_unit_types.default_bedroom_count,
       apartment_unit_types.default_bathroom_count)
    IS DISTINCT FROM
      (EXCLUDED.default_usable_area_m2, EXCLUDED.default_bedroom_count, EXCLUDED.default_bathroom_count);

DO $$
DECLARE guard_row record;
BEGIN
    SELECT * INTO guard_row FROM apartment_type_setup_guard;
    IF guard_row.apartment_digest IS DISTINCT FROM
       (SELECT md5(coalesce(jsonb_agg(to_jsonb(u) ORDER BY id)::text, '[]')) FROM apartments.apartment_units u)
    THEN RAISE EXCEPTION 'Apartment data unexpectedly changed; setup rolled back'; END IF;
    IF guard_row.pricing_digest IS DISTINCT FROM
       (SELECT md5(coalesce(jsonb_agg(to_jsonb(r) ORDER BY id)::text, '[]')) FROM billing.fee_rate_rules r)
    THEN RAISE EXCEPTION 'Pricing data unexpectedly changed; setup rolled back'; END IF;
END $$;

COMMIT;
