-- DEV-only, narrowly scoped reconciliation for RES-000003 -> RES-000002.
-- This script intentionally deletes the duplicate P202 ownership instead of ending or remapping it.
-- It is safe to rerun only after the expected successful outcome; any drift fails the transaction.

BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;

DO $reconcile$
DECLARE
    survivor_id constant uuid := 'b8fa807e-6935-4f27-96bc-a176abad651a';
    duplicate_id constant uuid := '2706d24e-26d5-498f-8a59-8fc557b2d8e2';
    duplicate_ownership_id constant uuid := '3708acdd-4747-448c-a2a8-b2f780865134';
    survivor_p202_ownership_id constant uuid := '97b5187e-bb46-4167-bc75-ae498e9d7e68';
    survivor_residency_id constant uuid := 'd27979c1-a96b-44f0-97a9-7bf19f08095f';
    p302_history_one constant uuid := '234d4c43-1c32-45ce-b532-3cadbef16f8a';
    p302_history_two constant uuid := 'd3491cf8-f229-46ea-87f2-695a95a496b3';
    ref record;
    ref_count bigint;
    affected_count bigint;
BEGIN
    IF current_database() NOT IN ('propflow', 'propflow_dev') THEN
        RAISE EXCEPTION 'Reconciliation is DEV-only; refusing database %', current_database();
    END IF;

    -- Lock both identities and all known related rows before any assertion or mutation.
    PERFORM 1
    FROM residents.residents
    WHERE id IN (survivor_id, duplicate_id)
    ORDER BY id
    FOR UPDATE;

    PERFORM 1
    FROM apartments.apartment_ownerships
    WHERE owner_resident_id IN (survivor_id, duplicate_id)
    ORDER BY id
    FOR UPDATE;

    PERFORM 1
    FROM residents.resident_apartments
    WHERE resident_id IN (survivor_id, duplicate_id)
    ORDER BY id
    FOR UPDATE;

    -- Idempotent success path: the duplicate is gone and every intended invariant already holds.
    IF NOT EXISTS (SELECT 1 FROM residents.residents WHERE id = duplicate_id) THEN
        IF NOT EXISTS (
            SELECT 1 FROM residents.residents
            WHERE id = survivor_id AND resident_code = 'RES-000002'
        ) OR (SELECT count(*) FROM apartments.apartment_ownerships ao
              JOIN apartments.apartment_units au ON au.id = ao.apartment_unit_id
              WHERE ao.owner_resident_id = survivor_id AND au.unit_number = 'P202' AND ao.end_date IS NULL) <> 1
        OR EXISTS (SELECT 1 FROM apartments.apartment_ownerships WHERE id = duplicate_ownership_id)
        OR (SELECT count(*) FROM residents.resident_apartments
            WHERE id = survivor_residency_id
              AND resident_id = survivor_id
              AND status = 'ACTIVE'
              AND end_date IS NULL) <> 1
        OR (SELECT count(*) FROM apartments.apartment_ownerships
            WHERE id IN (p302_history_one, p302_history_two)
              AND owner_resident_id = survivor_id
              AND end_date = DATE '2026-10-03') <> 2 THEN
            RAISE EXCEPTION 'RES-000003 is absent but the expected reconciled state is incomplete';
        END IF;

        FOR ref IN
            SELECT table_schema, table_name, column_name
            FROM information_schema.columns
            WHERE data_type = 'uuid'
              AND column_name LIKE '%resident%'
              AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name, column_name
        LOOP
            EXECUTE format('SELECT count(*) FROM %I.%I WHERE %I = $1', ref.table_schema, ref.table_name, ref.column_name)
                INTO ref_count USING duplicate_id;
            IF ref_count <> 0 THEN
                RAISE EXCEPTION 'RES-000003 is absent but %.%.% still has % reference(s)',
                    ref.table_schema, ref.table_name, ref.column_name, ref_count;
            END IF;
        END LOOP;

        RAISE NOTICE 'RES-000003 reconciliation already complete; no changes made';
        RETURN;
    END IF;

    IF (SELECT count(*) FROM residents.residents
        WHERE (id = survivor_id AND resident_code = 'RES-000002')
           OR (id = duplicate_id AND resident_code = 'RES-000003')) <> 2 THEN
        RAISE EXCEPTION 'Resident IDs/codes drifted from the audited pair';
    END IF;

    IF EXISTS (SELECT 1 FROM residents.residents WHERE id IN (survivor_id, duplicate_id) AND user_id IS NOT NULL) THEN
        RAISE EXCEPTION 'A resident acquired a UserId after audit';
    END IF;

    IF (SELECT count(DISTINCT upper(btrim(identity_type)) || ':' || regexp_replace(identity_number, '[^0-9]', '', 'g'))
        FROM residents.residents WHERE id IN (survivor_id, duplicate_id)) <> 1 THEN
        RAISE EXCEPTION 'Canonical identities no longer match';
    END IF;

    IF (SELECT count(DISTINCT lower(btrim(email)))
        FROM residents.residents WHERE id IN (survivor_id, duplicate_id)) <> 1 THEN
        RAISE EXCEPTION 'Normalized emails no longer match';
    END IF;

    IF (SELECT count(*) FROM residents.resident_apartments WHERE resident_id = duplicate_id) <> 0 THEN
        RAISE EXCEPTION 'Duplicate resident unexpectedly has residency rows';
    END IF;

    IF (SELECT count(*) FROM apartments.apartment_ownerships ao
        JOIN apartments.apartment_units au ON au.id = ao.apartment_unit_id
        WHERE ao.id = duplicate_ownership_id
          AND ao.owner_resident_id = duplicate_id
          AND au.unit_number = 'P202'
          AND ao.start_date = DATE '2026-10-03'
          AND ao.end_date IS NULL) <> 1 THEN
        RAISE EXCEPTION 'Duplicate P202 ownership no longer matches the audited row';
    END IF;

    IF (SELECT count(*) FROM apartments.apartment_ownerships ao
        JOIN apartments.apartment_units au ON au.id = ao.apartment_unit_id
        WHERE ao.id = survivor_p202_ownership_id
          AND ao.owner_resident_id = survivor_id
          AND au.unit_number = 'P202'
          AND ao.end_date IS NULL) <> 1 THEN
        RAISE EXCEPTION 'Survivor current P202 ownership no longer matches the audited row';
    END IF;

    IF (SELECT count(*) FROM residents.resident_apartments ra
        JOIN apartments.apartment_units au ON au.id = ra.apartment_unit_id
        WHERE ra.id = survivor_residency_id
          AND ra.resident_id = survivor_id
          AND au.unit_number = 'P202'
          AND ra.status = 'ACTIVE'
          AND ra.end_date IS NULL) <> 1 THEN
        RAISE EXCEPTION 'Survivor P202 residency no longer matches the audited row';
    END IF;

    IF (SELECT count(*) FROM apartments.apartment_ownerships
        WHERE id IN (p302_history_one, p302_history_two)
          AND owner_resident_id = survivor_id
          AND start_date = DATE '2026-10-03'
          AND end_date = DATE '2026-10-03') <> 2 THEN
        RAISE EXCEPTION 'Protected P302 history rows drifted from the audited state';
    END IF;

    -- Discover every UUID column whose name can reference a resident. Any new/unexpected link aborts.
    FOR ref IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE data_type = 'uuid'
          AND column_name LIKE '%resident%'
          AND table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, column_name
    LOOP
        EXECUTE format('SELECT count(*) FROM %I.%I WHERE %I = $1', ref.table_schema, ref.table_name, ref.column_name)
            INTO ref_count USING duplicate_id;

        IF ref.table_schema = 'apartments'
           AND ref.table_name = 'apartment_ownerships'
           AND ref.column_name = 'owner_resident_id' THEN
            IF ref_count <> 1 THEN
                RAISE EXCEPTION 'Expected exactly one duplicate ownership reference, found %', ref_count;
            END IF;
        ELSIF ref_count <> 0 THEN
            RAISE EXCEPTION 'Unexpected duplicate reference: %.%.% has % row(s)',
                ref.table_schema, ref.table_name, ref.column_name, ref_count;
        END IF;
    END LOOP;

    DELETE FROM apartments.apartment_ownerships
    WHERE id = duplicate_ownership_id
      AND owner_resident_id = duplicate_id;
    GET DIAGNOSTICS affected_count = ROW_COUNT;
    IF affected_count <> 1 THEN
        RAISE EXCEPTION 'Expected to delete one duplicate P202 ownership, deleted %', affected_count;
    END IF;

    -- The duplicate must now be completely unreferenced before deleting the profile.
    FOR ref IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE data_type = 'uuid'
          AND column_name LIKE '%resident%'
          AND table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, column_name
    LOOP
        EXECUTE format('SELECT count(*) FROM %I.%I WHERE %I = $1', ref.table_schema, ref.table_name, ref.column_name)
            INTO ref_count USING duplicate_id;
        IF ref_count <> 0 THEN
            RAISE EXCEPTION 'Reference remained after ownership cleanup: %.%.% has % row(s)',
                ref.table_schema, ref.table_name, ref.column_name, ref_count;
        END IF;
    END LOOP;

    DELETE FROM residents.residents
    WHERE id = duplicate_id
      AND resident_code = 'RES-000003';
    GET DIAGNOSTICS affected_count = ROW_COUNT;
    IF affected_count <> 1 THEN
        RAISE EXCEPTION 'Expected to delete one duplicate resident, deleted %', affected_count;
    END IF;

    -- Postconditions: survivor graph is intact and no duplicate canonical key remains.
    IF NOT EXISTS (SELECT 1 FROM residents.residents WHERE id = survivor_id AND resident_code = 'RES-000002')
       OR EXISTS (SELECT 1 FROM residents.residents WHERE id = duplicate_id OR resident_code = 'RES-000003') THEN
        RAISE EXCEPTION 'Resident postcondition failed';
    END IF;

    IF (SELECT count(*) FROM apartments.apartment_ownerships WHERE owner_resident_id = survivor_id) <> 4
       OR (SELECT count(*) FROM apartments.apartment_ownerships ao
           JOIN apartments.apartment_units au ON au.id = ao.apartment_unit_id
           WHERE ao.owner_resident_id = survivor_id AND au.unit_number = 'P202' AND ao.end_date IS NULL) <> 1
       OR (SELECT count(*) FROM apartments.apartment_ownerships
           WHERE id IN (p302_history_one, p302_history_two)
             AND owner_resident_id = survivor_id
             AND end_date = DATE '2026-10-03') <> 2 THEN
        RAISE EXCEPTION 'Ownership postcondition failed';
    END IF;

    IF (SELECT count(*) FROM residents.resident_apartments
        WHERE id = survivor_residency_id AND resident_id = survivor_id AND status = 'ACTIVE' AND end_date IS NULL) <> 1 THEN
        RAISE EXCEPTION 'Residency postcondition failed';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM residents.residents
        WHERE identity_type IS NOT NULL AND btrim(identity_type) <> ''
          AND identity_number IS NOT NULL AND regexp_replace(identity_number, '[^0-9]', '', 'g') <> ''
        GROUP BY upper(btrim(identity_type)), regexp_replace(identity_number, '[^0-9]', '', 'g')
        HAVING count(*) > 1
    ) OR EXISTS (
        SELECT 1
        FROM residents.residents
        WHERE email IS NOT NULL AND btrim(email) <> ''
        GROUP BY lower(btrim(email))
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Canonical duplicate remains after reconciliation';
    END IF;
END
$reconcile$;

COMMIT;
