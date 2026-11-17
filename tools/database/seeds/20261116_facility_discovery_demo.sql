-- Local demonstration data explicitly requested on 2026-11-16.
-- Run manually with psql against local propflow, never from application startup/migrations.
-- All records are marked DEMO. Re-running preserves existing records and Manager edits.
\set ON_ERROR_STOP on
SET client_encoding = 'UTF8';
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SELECT pg_advisory_xact_lock(71241002);

CREATE TEMP TABLE facility_demo_people ON COMMIT DROP AS
SELECT r.code AS role, u.id
FROM administration.user_role_assignments a
JOIN administration.roles r ON r.id = a.role_id
JOIN auth.users u ON u.id = a.user_id
WHERE r.code IN ('MANAGER', 'STAFF') AND r.is_active AND u.status = 'ACTIVE';

DO $$
BEGIN
    IF current_database() <> 'propflow' OR inet_server_addr() IS NOT NULL AND NOT
        (inet_server_addr() << inet '127.0.0.0/8' OR inet_server_addr() = inet '::1')
    THEN RAISE EXCEPTION 'This seed is restricted to local propflow'; END IF;
    IF NOT EXISTS (SELECT 1 FROM facility_demo_people WHERE role = 'MANAGER') OR
       NOT EXISTS (SELECT 1 FROM facility_demo_people WHERE role = 'STAFF')
    THEN RAISE EXCEPTION 'An active Manager and Staff are required; no accounts are created by this seed'; END IF;
END $$;

CREATE TEMP TABLE facility_demo_rows (
    code text, name text, location text, description text, audience text, minimum_age integer, maximum_age integer,
    child_policy text, supervision_note text, rules text, weekdays integer[], opens_at time, closes_at time,
    published boolean, status text, closed_dates date[]
) ON COMMIT DROP;

INSERT INTO facility_demo_rows VALUES
('DEMO-AI-KIDS', '[DEMO] Khu vui chơi trẻ em', 'Tầng 2 · Khu A (mẫu)',
 'DỮ LIỆU TEST. Khu vui chơi trong nhà có cầu trượt thấp, xếp hình, góc tô màu và vận động nhẹ cho trẻ 3–12 tuổi. Phù hợp gia đình đưa con vui chơi cuối tuần; không nhận trông trẻ khi bố mẹ vắng mặt.',
 'CHILDREN', 3, 12, 'GUARDIAN_REQUIRED', '',
 'Dữ liệu mẫu để kiểm thử. Phụ huynh/người giám hộ phải đi cùng và trực tiếp trông trẻ; không để trẻ ở lại một mình. Không mang đồ ăn vào khu trò chơi.',
 ARRAY[0,1,2,3,4,5,6], '08:00', '20:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-KIDS-CLUB', '[DEMO] CLB thiếu nhi thứ Bảy', 'Phòng sinh hoạt cộng đồng tầng 2 (mẫu)',
 'DỮ LIỆU TEST. Chương trình thiếu nhi có người phụ trách và giám sát cho trẻ 4–10 tuổi vào thứ Bảy: đọc truyện, vẽ, thủ công và trò chơi nhóm. Dành cho nhu cầu hoạt động cuối tuần khi bố mẹ đi làm; phải liên hệ BQL xác nhận điều kiện tiếp nhận trước.',
 'CHILDREN', 4, 10, 'SUPERVISED_CONTACT_REQUIRED',
 'Kịch bản mô phỏng chương trình có giám sát thứ Bảy 08:00–17:00. Cần xác nhận lịch, độ tuổi, khả năng tiếp nhận và người đón với BQL. Không phải cam kết có dịch vụ trông trẻ thực tế.',
 'Dữ liệu mẫu để kiểm thử. Chỉ tham gia sau khi người phụ trách xác nhận; phụ huynh bàn giao và đón trẻ theo hướng dẫn. Gợi ý AI không phải xác nhận đăng ký.',
 ARRAY[6], '08:00', '17:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-LIBRARY', '[DEMO] Thư viện và phòng đọc', 'Tầng 3 · Phòng đọc (mẫu)',
 'DỮ LIỆU TEST. Không gian yên tĩnh đọc sách, học tập và làm việc cá nhân, có bàn đọc và sách dùng tại chỗ. Phù hợp nhu cầu tập trung, tránh tiếng ồn; không phù hợp hoạt động vận động hoặc trông trẻ.',
 'GENERAL', NULL, NULL, 'GUARDIAN_REQUIRED', '',
 'Dữ liệu mẫu để kiểm thử. Giữ yên lặng, không gọi điện trong phòng đọc; trả sách đúng vị trí và giữ vệ sinh.',
 ARRAY[0,1,2,3,4,5,6], '06:00', '22:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-POOL', '[DEMO] Hồ bơi gia đình', 'Tầng 5 · Khu hồ bơi (mẫu)',
 'DỮ LIỆU TEST. Hồ bơi dành cho cư dân và gia đình từ 6 tuổi, phục vụ bơi lội và vận động dưới nước. Trẻ phải có người giám hộ đi cùng; nhân viên phụ trách không thay thế người trông trẻ. Đóng cửa thứ Hai để vệ sinh.',
 'FAMILY', 6, NULL, 'GUARDIAN_REQUIRED', '',
 'Dữ liệu mẫu để kiểm thử. Tuân thủ hướng dẫn an toàn và khu vực độ sâu phù hợp; không chạy trên sàn ướt. Không tự để trẻ xuống nước hoặc ở lại một mình.',
 ARRAY[0,2,3,4,5,6], '06:00', '20:00', true, 'ACTIVE', ARRAY[(date_trunc('week', now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date + 6)]),
('DEMO-AI-GYM', '[DEMO] Phòng gym người lớn', 'Tầng 4 · Phòng tập (mẫu)',
 'DỮ LIỆU TEST. Phòng gym dành cho cư dân từ 18 tuổi, có máy chạy bộ, xe đạp tập và khu tập sức mạnh. Phù hợp nhu cầu tập thể lực, không phải khu vui chơi cho trẻ em hoặc nơi giữ trẻ.',
 'ADULTS', 18, NULL, 'NOT_FOR_CHILDREN', '',
 'Dữ liệu mẫu để kiểm thử. Mang giày thể thao, sử dụng thiết bị đúng hướng dẫn và cất dụng cụ sau khi tập. Không cho trẻ em vào khu thiết bị.',
 ARRAY[0,1,2,3,4,5,6], '05:30', '22:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-WALK', '[DEMO] Vườn và đường đi bộ', 'Sân vườn tầng trệt (mẫu)',
 'DỮ LIỆU TEST. Lối đi bộ trong sân vườn, ghế nghỉ và khu cây xanh cho cư dân, gia đình thư giãn, đi dạo, vận động nhẹ ngoài trời. Trẻ em phải có người lớn đi cùng.',
 'FAMILY', NULL, NULL, 'GUARDIAN_REQUIRED', '',
 'Dữ liệu mẫu để kiểm thử. Giữ vệ sinh và lối đi thông thoáng; người giám hộ theo sát trẻ. Tạm dừng hoạt động ngoài trời khi thời tiết không phù hợp theo hướng dẫn BQL.',
 ARRAY[0,1,2,3,4,5,6], '05:00', '22:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-BADMINTON', '[DEMO] Sân cầu lông', 'Nhà thể thao tầng trệt (mẫu)',
 'DỮ LIỆU TEST. Sân cầu lông trong nhà cho cư dân từ 10 tuổi và gia đình luyện tập, chơi thể thao cùng bạn bè. Cần liên hệ người phụ trách để xác nhận việc sử dụng sân; không tự giữ chỗ qua AI.',
 'FAMILY', 10, NULL, 'GUARDIAN_REQUIRED', '',
 'Dữ liệu mẫu để kiểm thử. Mang giày phù hợp, tự chuẩn bị vợt và cầu; trẻ dưới 18 tuổi cần người giám hộ đi cùng. Dùng sân theo hướng dẫn BQL.',
 ARRAY[0,1,2,3,4,5,6], '06:00', '21:00', true, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-DRAFT', '[DEMO] Lớp kỹ năng chưa xuất bản', 'Phòng dự kiến (mẫu)',
 'DỮ LIỆU TEST ÂM. Hồ sơ lớp kỹ năng trẻ em còn ở trạng thái nháp, chưa công khai; không được đưa vào kết quả AI cho cư dân.',
 'CHILDREN', 4, 10, 'GUARDIAN_REQUIRED', '', 'Dữ liệu mẫu. Bản nháp, không nhận đăng ký.',
 ARRAY[6], '08:00', '11:00', false, 'ACTIVE', ARRAY[]::date[]),
('DEMO-AI-INACTIVE', '[DEMO] Phòng vui chơi ngừng hoạt động', 'Tầng 2 · Phòng đóng cửa (mẫu)',
 'DỮ LIỆU TEST ÂM. Phòng vui chơi đang ngừng hoạt động, không được gợi ý hoặc cho cư dân sử dụng.',
 'CHILDREN', 3, 12, 'GUARDIAN_REQUIRED', '', 'Dữ liệu mẫu. Đang ngừng hoạt động; không tiếp nhận cư dân.',
 ARRAY[0,6], '08:00', '17:00', false, 'INACTIVE', ARRAY[]::date[]);

CREATE TEMP TABLE facility_demo_ids ON COMMIT DROP AS
SELECT md5('propflow:facility-discovery-demo:v1:' || code)::uuid AS id, code FROM facility_demo_rows;
LOCK TABLE property_assets.facilities, property_assets.facility_guides IN SHARE ROW EXCLUSIVE MODE;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM property_assets.facilities f JOIN facility_demo_ids s ON f.code = s.code OR f.id = s.id
        WHERE f.id <> s.id OR f.code <> s.code)
    THEN RAISE EXCEPTION 'Reserved demo identifiers collide with another facility; nothing was changed'; END IF;
END $$;

CREATE TEMP TABLE facility_demo_preserve ON COMMIT DROP AS
SELECT
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(f) ORDER BY f.id)::text, '[]')) FROM property_assets.facilities f
        WHERE f.id NOT IN (SELECT id FROM facility_demo_ids)) AS other_facilities,
    (SELECT md5(coalesce(jsonb_agg(to_jsonb(g) ORDER BY g.facility_id)::text, '[]')) FROM property_assets.facility_guides g
        WHERE g.facility_id NOT IN (SELECT id FROM facility_demo_ids)) AS other_guides;

CREATE TEMP TABLE facility_demo_inserted ON COMMIT DROP AS
WITH inserted AS (
    INSERT INTO property_assets.facilities
        (id, code, name, location_description, description, status, created_by, updated_by, created_at, updated_at)
    SELECT s.id, r.code, r.name, r.location, r.description, r.status, actor.id, actor.id, now(), now()
    FROM facility_demo_rows r JOIN facility_demo_ids s USING (code)
    CROSS JOIN (SELECT id FROM facility_demo_people WHERE role = 'MANAGER' ORDER BY id LIMIT 1) actor
    ON CONFLICT (id) DO NOTHING RETURNING id, code
) SELECT * FROM inserted;

CREATE TEMP TABLE facility_demo_guides_inserted ON COMMIT DROP AS
WITH inserted AS (
    INSERT INTO property_assets.facility_guides
        (facility_id, published, description, audience, minimum_age, maximum_age, child_policy, supervision_note, rules,
         hours_json, closed_dates, responsible_staff_id, contact_details, token, updated_by, updated_at)
    SELECT s.id, r.published, r.description, r.audience, r.minimum_age, r.maximum_age, r.child_policy, r.supervision_note, r.rules,
        (SELECT jsonb_agg(jsonb_build_object('DayOfWeek', d, 'OpensAt', r.opens_at::text, 'ClosesAt', r.closes_at::text) ORDER BY d)
            FROM unnest(r.weekdays) d),
        r.closed_dates, staff.id, 'DỮ LIỆU TEST · Quầy BQL mẫu tầng 1, liên hệ qua chat BQL',
        gen_random_uuid(), actor.id, now()
    FROM facility_demo_rows r JOIN facility_demo_ids s USING (code)
    CROSS JOIN (SELECT id FROM facility_demo_people WHERE role = 'MANAGER' ORDER BY id LIMIT 1) actor
    CROSS JOIN (SELECT id FROM facility_demo_people WHERE role = 'STAFF' ORDER BY id LIMIT 1) staff
    ON CONFLICT (facility_id) DO NOTHING RETURNING facility_id
) SELECT * FROM inserted;

-- Null actor identifies a manual development import, not a Manager action in the application.
INSERT INTO administration.audit_logs
    (id, actor_user_id, action, entity_type, entity_id, new_values, correlation_id, created_at)
SELECT md5('propflow:facility-discovery-demo:audit:facility:v1:' || id::text)::uuid,
    NULL, 'IMPORT_DEMO_FACILITIES', 'Facility', id,
    jsonb_build_object('source', 'LOCAL_DEVELOPMENT_SEED', 'script', '20261116_facility_discovery_demo.sql', 'code', code),
    'facility-discovery-demo-v1', now() FROM facility_demo_inserted
ON CONFLICT (id) DO NOTHING;
INSERT INTO administration.audit_logs
    (id, actor_user_id, action, entity_type, entity_id, new_values, correlation_id, created_at)
SELECT md5('propflow:facility-discovery-demo:audit:guide:v1:' || facility_id::text)::uuid,
    NULL, 'IMPORT_DEMO_FACILITY_GUIDES', 'FacilityGuide', facility_id,
    jsonb_build_object('source', 'LOCAL_DEVELOPMENT_SEED', 'script', '20261116_facility_discovery_demo.sql'),
    'facility-discovery-demo-v1', now() FROM facility_demo_guides_inserted
ON CONFLICT (id) DO NOTHING;

DO $$
BEGIN
    IF (SELECT count(*) FROM property_assets.facilities WHERE id IN (SELECT id FROM facility_demo_ids)) <> 9 OR
       (SELECT count(*) FROM property_assets.facility_guides WHERE facility_id IN (SELECT id FROM facility_demo_ids)) <> 9
    THEN RAISE EXCEPTION 'Unexpected demo record count; import rolled back'; END IF;
    IF (SELECT other_facilities FROM facility_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(f) ORDER BY f.id)::text, '[]')) FROM property_assets.facilities f
            WHERE f.id NOT IN (SELECT id FROM facility_demo_ids)) OR
       (SELECT other_guides FROM facility_demo_preserve) IS DISTINCT FROM
        (SELECT md5(coalesce(jsonb_agg(to_jsonb(g) ORDER BY g.facility_id)::text, '[]')) FROM property_assets.facility_guides g
            WHERE g.facility_id NOT IN (SELECT id FROM facility_demo_ids))
    THEN RAISE EXCEPTION 'An existing facility/profile changed; import rolled back'; END IF;
END $$;

SELECT (SELECT count(*) FROM facility_demo_inserted) AS facilities_inserted,
    (SELECT count(*) FROM facility_demo_guides_inserted) AS guides_inserted;
COMMIT;
