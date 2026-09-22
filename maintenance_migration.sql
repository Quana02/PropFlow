CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'maintenance') THEN
            CREATE SCHEMA maintenance;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE TABLE maintenance.maintenance_schedules (
        id uuid NOT NULL,
        schedule_code character varying(30) NOT NULL,
        building_id uuid NOT NULL,
        facility_id uuid,
        equipment_id uuid,
        title character varying(200) NOT NULL,
        description text,
        planned_start_at timestamp with time zone NOT NULL,
        planned_end_at timestamp with time zone,
        status character varying(30) NOT NULL DEFAULT 'ACTIVE',
        created_by uuid NOT NULL,
        updated_by uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_maintenance_schedules" PRIMARY KEY (id),
        CONSTRAINT "CK_maintenance_schedules_planned_end_at" CHECK (planned_end_at IS NULL OR planned_end_at >= planned_start_at)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE TABLE maintenance.maintenance_tasks (
        id uuid NOT NULL,
        task_number character varying(30) NOT NULL,
        schedule_id uuid,
        source_service_request_id uuid,
        source_complaint_id uuid,
        building_id uuid NOT NULL,
        facility_id uuid,
        equipment_id uuid,
        title character varying(200) NOT NULL,
        description text,
        priority_code character varying(30),
        status character varying(30) NOT NULL DEFAULT 'OPEN',
        planned_start_at timestamp with time zone,
        due_at timestamp with time zone,
        started_at timestamp with time zone,
        completed_at timestamp with time zone,
        closed_at timestamp with time zone,
        closed_by uuid,
        created_by uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_maintenance_tasks" PRIMARY KEY (id),
        CONSTRAINT "CK_maintenance_tasks_closed_at" CHECK (completed_at IS NULL OR closed_at IS NULL OR closed_at >= completed_at),
        CONSTRAINT "CK_maintenance_tasks_completed_at" CHECK (started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at),
        CONSTRAINT "CK_maintenance_tasks_due_at" CHECK (planned_start_at IS NULL OR due_at IS NULL OR due_at >= planned_start_at),
        CONSTRAINT "FK_maintenance_tasks_maintenance_schedules_schedule_id" FOREIGN KEY (schedule_id) REFERENCES maintenance.maintenance_schedules (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE TABLE maintenance.maintenance_assignments (
        id uuid NOT NULL,
        maintenance_task_id uuid NOT NULL,
        staff_user_id uuid NOT NULL,
        assigned_by uuid NOT NULL,
        status character varying(30) NOT NULL DEFAULT 'ASSIGNED',
        assignment_note text,
        assigned_at timestamp with time zone NOT NULL DEFAULT (now()),
        started_at timestamp with time zone,
        completed_at timestamp with time zone,
        ended_at timestamp with time zone,
        CONSTRAINT "PK_maintenance_assignments" PRIMARY KEY (id),
        CONSTRAINT "CK_maintenance_assignments_completed_at" CHECK (started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at),
        CONSTRAINT "CK_maintenance_assignments_ended_at" CHECK (ended_at IS NULL OR ended_at >= assigned_at),
        CONSTRAINT "CK_maintenance_assignments_started_at" CHECK (started_at IS NULL OR started_at >= assigned_at),
        CONSTRAINT "FK_maintenance_assignments_maintenance_tasks_maintenance_task_~" FOREIGN KEY (maintenance_task_id) REFERENCES maintenance.maintenance_tasks (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE TABLE maintenance.maintenance_results (
        id uuid NOT NULL,
        maintenance_task_id uuid NOT NULL,
        attempt_no integer NOT NULL DEFAULT 1,
        submitted_by uuid NOT NULL,
        summary text NOT NULL,
        work_performed text,
        issue_found text,
        parts_or_resources_used text,
        recommendation text,
        result_status character varying(30) NOT NULL DEFAULT 'SUBMITTED',
        reviewed_by uuid,
        review_note text,
        submitted_at timestamp with time zone NOT NULL DEFAULT (now()),
        reviewed_at timestamp with time zone,
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_maintenance_results" PRIMARY KEY (id),
        CONSTRAINT "CK_maintenance_results_attempt_no" CHECK (attempt_no >= 1),
        CONSTRAINT "CK_maintenance_results_reviewed_at" CHECK (reviewed_at IS NULL OR reviewed_at >= submitted_at),
        CONSTRAINT "FK_maintenance_results_maintenance_tasks_maintenance_task_id" FOREIGN KEY (maintenance_task_id) REFERENCES maintenance.maintenance_tasks (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE TABLE maintenance.maintenance_task_activities (
        id uuid NOT NULL,
        maintenance_task_id uuid NOT NULL,
        assignment_id uuid,
        activity_type character varying(30) NOT NULL,
        from_status character varying(30),
        to_status character varying(30),
        detail text,
        performed_by uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_maintenance_task_activities" PRIMARY KEY (id),
        CONSTRAINT "FK_maintenance_task_activities_maintenance_assignments_assignm~" FOREIGN KEY (assignment_id) REFERENCES maintenance.maintenance_assignments (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_maintenance_task_activities_maintenance_tasks_maintenance_t~" FOREIGN KEY (maintenance_task_id) REFERENCES maintenance.maintenance_tasks (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE UNIQUE INDEX "IX_maintenance_assignments_active_maintenance_task_id" ON maintenance.maintenance_assignments (maintenance_task_id) WHERE "status" IN ('ASSIGNED', 'IN_PROGRESS');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_assignments_assigned_at" ON maintenance.maintenance_assignments (assigned_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_assignments_staff_user_id" ON maintenance.maintenance_assignments (staff_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_assignments_status" ON maintenance.maintenance_assignments (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE UNIQUE INDEX "IX_maintenance_results_maintenance_task_id_attempt_no" ON maintenance.maintenance_results (maintenance_task_id, attempt_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_results_result_status" ON maintenance.maintenance_results (result_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_results_reviewed_by" ON maintenance.maintenance_results (reviewed_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_schedules_building_id" ON maintenance.maintenance_schedules (building_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_schedules_equipment_id" ON maintenance.maintenance_schedules (equipment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_schedules_facility_id" ON maintenance.maintenance_schedules (facility_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_schedules_planned_start_at" ON maintenance.maintenance_schedules (planned_start_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE UNIQUE INDEX "IX_maintenance_schedules_schedule_code" ON maintenance.maintenance_schedules (schedule_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_schedules_status" ON maintenance.maintenance_schedules (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_task_activities_activity_type" ON maintenance.maintenance_task_activities (activity_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_task_activities_assignment_id" ON maintenance.maintenance_task_activities (assignment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_task_activities_created_at" ON maintenance.maintenance_task_activities (created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_task_activities_maintenance_task_id" ON maintenance.maintenance_task_activities (maintenance_task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_building_id" ON maintenance.maintenance_tasks (building_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_due_at" ON maintenance.maintenance_tasks (due_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_equipment_id" ON maintenance.maintenance_tasks (equipment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_priority_code" ON maintenance.maintenance_tasks (priority_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_schedule_id" ON maintenance.maintenance_tasks (schedule_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_source_complaint_id" ON maintenance.maintenance_tasks (source_complaint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_source_service_request_id" ON maintenance.maintenance_tasks (source_service_request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE INDEX "IX_maintenance_tasks_status" ON maintenance.maintenance_tasks (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    CREATE UNIQUE INDEX "IX_maintenance_tasks_task_number" ON maintenance.maintenance_tasks (task_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260914160954_InitialMaintenance') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260914160954_InitialMaintenance', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921140942_RemoveBuildingScopeFromMaintenance') THEN
    DROP INDEX maintenance."IX_maintenance_tasks_building_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921140942_RemoveBuildingScopeFromMaintenance') THEN
    DROP INDEX maintenance."IX_maintenance_schedules_building_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921140942_RemoveBuildingScopeFromMaintenance') THEN
    ALTER TABLE maintenance.maintenance_tasks DROP COLUMN building_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921140942_RemoveBuildingScopeFromMaintenance') THEN
    ALTER TABLE maintenance.maintenance_schedules DROP COLUMN building_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921140942_RemoveBuildingScopeFromMaintenance') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921140942_RemoveBuildingScopeFromMaintenance', '8.0.11');
    END IF;
END $EF$;
COMMIT;

