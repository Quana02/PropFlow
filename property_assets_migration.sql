CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'property_assets') THEN
            CREATE SCHEMA property_assets;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE TABLE property_assets.buildings (
        id uuid NOT NULL,
        code character varying(30) NOT NULL,
        name character varying(150) NOT NULL,
        address text NOT NULL,
        time_zone_id character varying(64) NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
        number_of_floors integer,
        description text,
        status character varying(20) NOT NULL DEFAULT 'ACTIVE',
        created_by uuid,
        updated_by uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_buildings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE TABLE property_assets.facilities (
        id uuid NOT NULL,
        building_id uuid NOT NULL,
        code character varying(30) NOT NULL,
        name character varying(150) NOT NULL,
        facility_type character varying(80),
        location_description character varying(255),
        description text,
        status character varying(20) NOT NULL DEFAULT 'ACTIVE',
        created_by uuid,
        updated_by uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_facilities" PRIMARY KEY (id),
        CONSTRAINT "FK_facilities_buildings_building_id" FOREIGN KEY (building_id) REFERENCES property_assets.buildings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE TABLE property_assets.equipment (
        id uuid NOT NULL,
        building_id uuid NOT NULL,
        facility_id uuid,
        code character varying(50) NOT NULL,
        name character varying(150) NOT NULL,
        equipment_type character varying(100),
        manufacturer character varying(100),
        model character varying(100),
        serial_number character varying(100),
        installation_date date,
        warranty_expiry_date date,
        location_description character varying(255),
        status character varying(30) NOT NULL DEFAULT 'ACTIVE',
        description text,
        created_by uuid,
        updated_by uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_equipment" PRIMARY KEY (id),
        CONSTRAINT "FK_equipment_buildings_building_id" FOREIGN KEY (building_id) REFERENCES property_assets.buildings (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_equipment_facilities_facility_id" FOREIGN KEY (facility_id) REFERENCES property_assets.facilities (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE UNIQUE INDEX "IX_buildings_code" ON property_assets.buildings (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_buildings_name" ON property_assets.buildings (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_buildings_status" ON property_assets.buildings (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE UNIQUE INDEX "IX_equipment_building_id_code" ON property_assets.equipment (building_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_equipment_equipment_type" ON property_assets.equipment (equipment_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_equipment_facility_id" ON property_assets.equipment (facility_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_equipment_status" ON property_assets.equipment (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_facilities_building_id" ON property_assets.facilities (building_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE UNIQUE INDEX "IX_facilities_building_id_code" ON property_assets.facilities (building_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_facilities_facility_type" ON property_assets.facilities (facility_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    CREATE INDEX "IX_facilities_status" ON property_assets.facilities (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914063326_InitialPropertyAssets') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260914063326_InitialPropertyAssets', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    ALTER TABLE property_assets.equipment DROP CONSTRAINT "FK_equipment_buildings_building_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    ALTER TABLE property_assets.facilities DROP CONSTRAINT "FK_facilities_buildings_building_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    DROP INDEX property_assets."IX_facilities_building_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    DROP INDEX property_assets."IX_facilities_building_id_code";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    DROP INDEX property_assets."IX_equipment_building_id_code";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    ALTER TABLE property_assets.facilities DROP COLUMN building_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    ALTER TABLE property_assets.equipment DROP COLUMN building_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    CREATE UNIQUE INDEX "IX_facilities_code" ON property_assets.facilities (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    CREATE UNIQUE INDEX "IX_equipment_code" ON property_assets.equipment (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921135656_ConvertPropertyAssetsToSingleBuilding') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921135656_ConvertPropertyAssetsToSingleBuilding', '8.0.11');
    END IF;
END $EF$;
COMMIT;

