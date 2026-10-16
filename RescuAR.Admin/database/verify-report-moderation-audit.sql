-- Read-only catalog verification. Does not invoke app RPCs or read log/user rows.
WITH expected_functions(signature, definer, api, body_fingerprint) AS (
    VALUES
        ('public.admin_moderate_report(uuid,text,text,text)', true, true, '184e2f71c7ff9eb7ebcce13d7f3fe770'),
        ('audit_private.prevent_event_change()', false, false, 'b07f811e22d9e5ea9de2984ec95dca3c'),
        ('public.web_admin_access()', true, true, 'ad8b8ac3c0880fb3d370908b1c24b720'),
        ('audit_private.require_admin()', false, false, 'fe9c3f863b99691cc89aed0b8db129e0'),
        ('audit_private.append_event(uuid,uuid,text,text,text,text,text,text,jsonb)', false, false, '590acd7b43b4f1c734fe4e8d255225bd'),
        ('public.record_web_audit_event(uuid,text,text,text,text,jsonb)', true, true, 'b3fabf1b5437293f503ffade444d3f90'),
        ('public.get_web_audit_logs(integer,timestamptz,uuid,text,uuid,text)', true, true, 'cc00838ac97a3b2309cfefb0026e7389'),
        ('public.admin_update_user(uuid,jsonb)', true, true, '605a0edea7e78e82f458d3dc4981936e'),
        ('public.admin_toggle_suspend_user(uuid,text)', true, true, '01781a74a1412ae70e6082138a92839d'),
        ('public.admin_delete_user(uuid)', true, true, '376c89f3d05b119e2c0d7ef73dc88cb1'),
        ('public.get_all_users_with_roles()', true, true, '762901f0a24a27ad16504438a3a77dbe')
), function_state AS (
    SELECT e.*, p.oid, pg_catalog.pg_get_userbyid(p.proowner) AS owner,
        p.prosecdef, p.proconfig,
        pg_catalog.md5(pg_catalog.regexp_replace(p.prosrc, '\s+', ' ', 'g')) AS actual_fingerprint
    FROM expected_functions e
    LEFT JOIN pg_catalog.pg_proc p ON p.oid = pg_catalog.to_regprocedure(e.signature)
), event_table AS (
    SELECT c.* FROM pg_catalog.pg_class c
    WHERE c.oid = pg_catalog.to_regclass('audit_private.events')
), roles AS (
    SELECT oid, rolname FROM pg_catalog.pg_roles
    WHERE rolname IN ('anon', 'authenticated', 'service_role')
), checks AS (
    SELECT 'private_schema' AS name, COALESCE((
        SELECT pg_catalog.pg_get_userbyid(n.nspowner) = 'postgres'
            AND pg_catalog.obj_description(n.oid, 'pg_namespace') = 'RescuAR web audit foundation v1'
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(n.nspacl) g
                WHERE g.grantee <> n.nspowner)
        FROM pg_catalog.pg_namespace n WHERE n.nspname = 'audit_private'
    ), false) AS passed
    UNION ALL SELECT 'storage_rls_and_owner', EXISTS (
        SELECT 1 FROM event_table t WHERE t.relrowsecurity
            AND pg_catalog.pg_get_userbyid(t.relowner) = 'postgres'
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = t.oid)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) g WHERE g.grantee <> t.relowner)
    )
    UNION ALL SELECT 'api_roles_have_no_direct_storage_access',
        (SELECT count(*) FROM roles) = 3 AND EXISTS (SELECT 1 FROM event_table)
        AND NOT EXISTS (SELECT 1 FROM event_table t CROSS JOIN roles r
            WHERE pg_catalog.has_table_privilege(r.oid, t.oid,
                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'))
    UNION ALL SELECT 'history_has_no_account_foreign_keys', EXISTS (SELECT 1 FROM event_table)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
            WHERE c.conrelid = pg_catalog.to_regclass('audit_private.events') AND c.contype = 'f')
    UNION ALL SELECT 'immutability_triggers', (
        SELECT count(*) = 2 FROM pg_catalog.pg_trigger t
        WHERE t.tgrelid = pg_catalog.to_regclass('audit_private.events') AND t.tgenabled = 'O'
            AND t.tgfoid = pg_catalog.to_regprocedure('audit_private.prevent_event_change()')
            AND ((t.tgname = 'events_no_update_delete' AND t.tgtype = 27)
                OR (t.tgname = 'events_no_truncate' AND t.tgtype = 34))
    )
    UNION ALL SELECT 'retry_key', EXISTS (
        SELECT 1 FROM pg_catalog.pg_constraint c
        WHERE c.conrelid = pg_catalog.to_regclass('audit_private.events')
            AND c.conname = 'events_retry_key' AND c.contype = 'u'
            AND pg_catalog.pg_get_constraintdef(c.oid) = 'UNIQUE (actor_id, operation_id, event_type, source)'
    )
    UNION ALL SELECT 'functions_match_reviewed_contracts', COALESCE((
        SELECT pg_catalog.bool_and(oid IS NOT NULL AND owner = 'postgres'
            AND prosecdef = definer AND proconfig = ARRAY['search_path=""']::text[]
            AND actual_fingerprint = body_fingerprint) FROM function_state
    ), false)
    UNION ALL SELECT 'function_execution_is_restricted',
        (SELECT count(*) FROM roles) = 3 AND COALESCE((
            SELECT pg_catalog.bool_and(f.oid IS NOT NULL AND
                pg_catalog.has_function_privilege(r.oid, f.oid, 'EXECUTE') =
                    (f.api AND r.rolname = 'authenticated'))
            FROM function_state f CROSS JOIN roles r
        ), false)
    UNION ALL SELECT 'membership_table_wide_privileges_revoked',
        pg_catalog.to_regclass('public.admin_roles') IS NOT NULL
        AND (SELECT count(*) FROM roles WHERE rolname IN ('anon', 'authenticated')) = 2
        AND NOT EXISTS (SELECT 1 FROM roles r WHERE r.rolname IN ('anon', 'authenticated')
            AND pg_catalog.has_table_privilege(r.oid, pg_catalog.to_regclass('public.admin_roles'),
                'TRUNCATE,REFERENCES,TRIGGER'))
    UNION ALL SELECT 'moderation_rpc_contract',
        (SELECT count(*) = 1 FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public' AND p.proname = 'admin_moderate_report')
        AND EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
            WHERE p.oid = pg_catalog.to_regprocedure('public.admin_moderate_report(uuid,text,text,text)')
                AND p.prokind = 'f' AND p.pronargdefaults = 0
                AND pg_catalog.pg_get_function_identity_arguments(p.oid) =
                    'p_operation_id uuid, p_report_id text, p_expected_status text, p_new_status text'
                AND pg_catalog.pg_get_function_result(p.oid) =
                    'TABLE(report_id text, before_status text, after_status text, outcome text, audit_event_id uuid, recorded_at timestamp with time zone)')
    UNION ALL SELECT 'report_contract_and_triggers_preserved', EXISTS (
        SELECT 1 FROM pg_catalog.pg_class c
        WHERE c.oid = pg_catalog.to_regclass('public.community_reports') AND c.relkind = 'r'
            AND c.relrowsecurity AND NOT c.relforcerowsecurity
            AND pg_catalog.pg_get_userbyid(c.relowner) = 'postgres'
    ) AND NOT EXISTS (SELECT 1 FROM (VALUES ('id'), ('status')) required(column_name)
        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
            WHERE a.attrelid = pg_catalog.to_regclass('public.community_reports')
                AND a.attname = required.column_name AND NOT a.attisdropped
                AND a.atttypid = 'text'::regtype AND a.attgenerated = ''))
        AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
            WHERE c.conrelid = pg_catalog.to_regclass('public.community_reports')
                AND c.contype = 'p' AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
            WHERE t.tgrelid = pg_catalog.to_regclass('public.community_reports') AND NOT t.tgisinternal)
    UNION ALL SELECT 'shared_report_policies_preserved',
        (SELECT count(*) = 3 FROM pg_catalog.pg_policy p
            WHERE p.polrelid = pg_catalog.to_regclass('public.community_reports'))
        AND NOT EXISTS (SELECT 1 FROM (VALUES
            ('Allow public insert access', 'a', NULL::text, 'true'),
            ('Allow public read access', 'r', 'true', NULL::text),
            ('Allow public update access', 'w', 'true', NULL::text)
        ) expected(name, command, using_expression, check_expression)
        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
            WHERE p.polrelid = pg_catalog.to_regclass('public.community_reports')
                AND p.polname = expected.name AND p.polcmd::text = expected.command
                AND p.polpermissive AND p.polroles = ARRAY[0]::oid[]
                AND pg_catalog.pg_get_expr(p.polqual, p.polrelid) IS NOT DISTINCT FROM expected.using_expression
                AND pg_catalog.pg_get_expr(p.polwithcheck, p.polrelid) IS NOT DISTINCT FROM expected.check_expression))
    UNION ALL SELECT 'shared_report_grants_preserved',
        (SELECT count(*) = 3 FROM roles)
        AND pg_catalog.to_regclass('public.community_reports') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM roles r
            CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'),
                ('TRUNCATE'), ('REFERENCES'), ('TRIGGER')) expected(privilege)
            WHERE NOT COALESCE(pg_catalog.has_table_privilege(r.oid,
                pg_catalog.to_regclass('public.community_reports'), expected.privilege), false))
)
SELECT pg_catalog.jsonb_pretty(pg_catalog.jsonb_build_object(
    'foundation_version', 1,
    'moderation_version', 1,
    'coverage', 'Moderation RPC only; existing public report writes still bypass its audit capture',
    'postgres_version', pg_catalog.current_setting('server_version'),
    'all_checks_passed', (SELECT pg_catalog.bool_and(passed) FROM checks),
    'checks', (SELECT pg_catalog.jsonb_object_agg(name, passed) FROM checks),
    'functions', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
        'signature', signature, 'exists', oid IS NOT NULL, 'owner', owner,
        'security_definer', prosecdef, 'settings', proconfig,
        'body_matches', actual_fingerprint = body_fingerprint,
        'role_access', (SELECT pg_catalog.jsonb_object_agg(r.rolname,
            pg_catalog.has_function_privilege(r.oid, f.oid, 'EXECUTE')) FROM roles r)
    ) ORDER BY signature) FROM function_state f),
    'storage', (SELECT pg_catalog.jsonb_build_object(
        'rls_enabled', t.relrowsecurity,
        'columns', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'name', a.attname, 'type', pg_catalog.format_type(a.atttypid, a.atttypmod),
            'not_null', a.attnotnull, 'default', pg_catalog.pg_get_expr(d.adbin, d.adrelid)
        ) ORDER BY a.attnum) FROM pg_catalog.pg_attribute a
            LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped),
        'constraints', (SELECT pg_catalog.jsonb_agg(pg_catalog.pg_get_constraintdef(c.oid))
            FROM pg_catalog.pg_constraint c WHERE c.conrelid = t.oid),
        'indexes', (SELECT pg_catalog.jsonb_agg(pg_catalog.pg_get_indexdef(i.indexrelid))
            FROM pg_catalog.pg_index i WHERE i.indrelid = t.oid),
        'triggers', (SELECT pg_catalog.jsonb_agg(pg_catalog.pg_get_triggerdef(g.oid, true))
            FROM pg_catalog.pg_trigger g WHERE g.tgrelid = t.oid AND NOT g.tgisinternal)
    ) FROM event_table t)
)) AS report_moderation_audit_report;
