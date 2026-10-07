-- Audit Logs, step 1. Run this entire query in the project's Supabase SQL Editor.
-- Returns catalog metadata only, with no application rows or database changes.
-- Review definitions/defaults for embedded credentials before sharing the result.

WITH expected_relations(schema_name, relation_name) AS (
    VALUES
        ('public', 'admin_roles'), ('public', 'users'),
        ('public', 'advisories'), ('public', 'community_reports'),
        ('public', 'evacuation_centers'), ('public', 'emergency_hotlines'),
        ('public', 'system_settings'), ('public', 'monitoring_stations'),
        ('public', 'river_level_history'),
        ('auth', 'users'), ('auth', 'audit_log_entries')
), target_relations AS (
    SELECT c.*, n.nspname AS schema_name
    FROM pg_catalog.pg_class AS c
    JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
        AND n.nspname NOT IN ('pg_catalog', 'information_schema')
        AND n.nspname NOT LIKE 'pg_toast%'
        AND n.nspname NOT LIKE 'pg_temp%'
        AND (
            EXISTS (
                SELECT 1 FROM expected_relations AS e
                WHERE e.schema_name = n.nspname AND e.relation_name = c.relname
            )
            OR n.nspname IN ('audit', 'audit_private')
            OR c.relname ILIKE ANY (ARRAY[
                '%audit%', '%activity_log%', '%admin_log%', '%system_log%'
            ])
        )
), target_policies AS (
    SELECT p.* FROM pg_catalog.pg_policy AS p
    JOIN target_relations AS r ON r.oid = p.polrelid
), relevant_function_oids AS (
    SELECT p.oid FROM pg_catalog.pg_proc AS p
    JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
        AND n.nspname NOT LIKE 'pg_toast%'
        AND n.nspname NOT LIKE 'pg_temp%'
        AND (
            p.proname IN ('get_all_users_with_roles', 'is_admin', 'is_administrator',
                'has_admin_access', 'has_role', 'authorize')
            OR p.proname LIKE 'admin\_%' ESCAPE '\'
            OR p.proname ILIKE '%audit%'
        )
    UNION
    SELECT t.tgfoid FROM pg_catalog.pg_trigger AS t
    JOIN target_relations AS r ON r.oid = t.tgrelid
    WHERE NOT t.tgisinternal
    UNION
    SELECT d.refobjid FROM pg_catalog.pg_depend AS d
    JOIN target_policies AS p ON p.oid = d.objid
    WHERE d.classid = 'pg_catalog.pg_policy'::regclass
        AND d.refclassid = 'pg_catalog.pg_proc'::regclass
), relevant_functions AS (
    SELECT p.*, n.nspname AS schema_name
    FROM pg_catalog.pg_proc AS p
    JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
    JOIN relevant_function_oids AS f ON f.oid = p.oid
    WHERE p.prokind IN ('f', 'p')
), application_roles AS (
    SELECT oid, rolname, rolbypassrls FROM pg_catalog.pg_roles
    WHERE rolname IN ('anon', 'authenticated', 'service_role')
)
SELECT pg_catalog.jsonb_pretty(pg_catalog.jsonb_build_object(
    'report_version', 1,
    'postgres_version', pg_catalog.current_setting('server_version'),
    'expected_relations', (
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'schema', e.schema_name, 'name', e.relation_name,
            'exists', EXISTS (
                SELECT 1 FROM target_relations AS r
                WHERE r.schema_name = e.schema_name AND r.relname = e.relation_name
            )
        ) ORDER BY e.schema_name, e.relation_name)
        FROM expected_relations AS e
    ),
    'relations', COALESCE((
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'schema', r.schema_name, 'name', r.relname, 'kind', r.relkind,
            'owner', pg_catalog.pg_get_userbyid(r.relowner),
            'rls_enabled', r.relrowsecurity, 'rls_forced', r.relforcerowsecurity,
            'options', r.reloptions,
            'view_definition', CASE WHEN r.relkind IN ('v', 'm')
                THEN pg_catalog.pg_get_viewdef(r.oid, true) ELSE NULL END,
            'columns', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'name', a.attname,
                    'type', pg_catalog.format_type(a.atttypid, a.atttypmod),
                    'not_null', a.attnotnull, 'identity', a.attidentity,
                    'generated', a.attgenerated,
                    'default', pg_catalog.pg_get_expr(d.adbin, d.adrelid),
                    'enum_values', (
                        SELECT pg_catalog.jsonb_agg(e.enumlabel ORDER BY e.enumsortorder)
                        FROM pg_catalog.pg_enum AS e WHERE e.enumtypid = a.atttypid
                    )
                ) ORDER BY a.attnum)
                FROM pg_catalog.pg_attribute AS a
                LEFT JOIN pg_catalog.pg_attrdef AS d
                    ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                WHERE a.attrelid = r.oid AND a.attnum > 0 AND NOT a.attisdropped
            ), '[]'::jsonb),
            'indexes', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.pg_get_indexdef(i.indexrelid)
                    ORDER BY i.indexrelid)
                FROM pg_catalog.pg_index AS i WHERE i.indrelid = r.oid
            ), '[]'::jsonb),
            'grants', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'grantee', CASE WHEN g.grantee = 0 THEN 'PUBLIC'
                        ELSE pg_catalog.pg_get_userbyid(g.grantee) END,
                    'privilege', g.privilege_type, 'grantable', g.is_grantable
                ) ORDER BY g.grantee, g.privilege_type)
                FROM pg_catalog.aclexplode(COALESCE(r.relacl,
                    pg_catalog.acldefault('r', r.relowner))) AS g
            ), '[]'::jsonb),
            'column_grants', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'column', a.attname,
                    'grantee', CASE WHEN g.grantee = 0 THEN 'PUBLIC'
                        ELSE pg_catalog.pg_get_userbyid(g.grantee) END,
                    'privilege', g.privilege_type, 'grantable', g.is_grantable
                ) ORDER BY a.attnum, g.grantee, g.privilege_type)
                FROM pg_catalog.pg_attribute AS a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) AS g
                WHERE a.attrelid = r.oid AND a.attnum > 0 AND NOT a.attisdropped
            ), '[]'::jsonb),
            'role_access', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'role', a.rolname,
                    'select', pg_catalog.has_table_privilege(a.oid, r.oid, 'SELECT'),
                    'insert', pg_catalog.has_table_privilege(a.oid, r.oid, 'INSERT'),
                    'update', pg_catalog.has_table_privilege(a.oid, r.oid, 'UPDATE'),
                    'delete', pg_catalog.has_table_privilege(a.oid, r.oid, 'DELETE'),
                    'truncate', pg_catalog.has_table_privilege(a.oid, r.oid, 'TRUNCATE')
                ) ORDER BY a.rolname) FROM application_roles AS a
            ), '[]'::jsonb),
            'triggers', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'name', t.tgname, 'enabled', t.tgenabled,
                    'definition', pg_catalog.pg_get_triggerdef(t.oid, true),
                    'function', t.tgfoid::regprocedure::text
                ) ORDER BY t.tgname)
                FROM pg_catalog.pg_trigger AS t
                WHERE t.tgrelid = r.oid AND NOT t.tgisinternal
            ), '[]'::jsonb)
        ) ORDER BY r.schema_name, r.relname)
        FROM target_relations AS r
    ), '[]'::jsonb),
    'constraints', COALESCE((
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'name', c.conname, 'relation', c.conrelid::regclass::text,
            'referenced_relation', CASE WHEN c.confrelid <> 0
                THEN c.confrelid::regclass::text ELSE NULL END,
            'definition', pg_catalog.pg_get_constraintdef(c.oid, true)
        ) ORDER BY c.conrelid, c.conname)
        FROM pg_catalog.pg_constraint AS c
        WHERE c.conrelid IN (SELECT oid FROM target_relations)
            OR c.confrelid IN (SELECT oid FROM target_relations)
    ), '[]'::jsonb),
    'policies', COALESCE((
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'relation', p.polrelid::regclass::text, 'name', p.polname,
            'command', p.polcmd, 'permissive', p.polpermissive,
            'roles', (
                SELECT pg_catalog.jsonb_agg(CASE WHEN role_id = 0 THEN 'PUBLIC'
                    ELSE pg_catalog.pg_get_userbyid(role_id) END ORDER BY role_id)
                FROM pg_catalog.unnest(p.polroles) AS policy_role(role_id)
            ),
            'using', pg_catalog.pg_get_expr(p.polqual, p.polrelid),
            'with_check', pg_catalog.pg_get_expr(p.polwithcheck, p.polrelid)
        ) ORDER BY p.polrelid, p.polname) FROM target_policies AS p
    ), '[]'::jsonb),
    'functions', COALESCE((
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'schema', f.schema_name, 'name', f.proname,
            'identity_arguments', pg_catalog.pg_get_function_identity_arguments(f.oid),
            'result_type', pg_catalog.pg_get_function_result(f.oid),
            'owner', pg_catalog.pg_get_userbyid(f.proowner),
            'security_definer', f.prosecdef, 'volatility', f.provolatile,
            'settings', f.proconfig,
            'grants', (
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'grantee', CASE WHEN g.grantee = 0 THEN 'PUBLIC'
                        ELSE pg_catalog.pg_get_userbyid(g.grantee) END,
                    'privilege', g.privilege_type
                ) ORDER BY g.grantee, g.privilege_type)
                FROM pg_catalog.aclexplode(COALESCE(f.proacl,
                    pg_catalog.acldefault('f', f.proowner))) AS g
            ),
            'role_access', COALESCE((
                SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'role', a.rolname,
                    'execute', pg_catalog.has_function_privilege(a.oid, f.oid, 'EXECUTE')
                ) ORDER BY a.rolname) FROM application_roles AS a
            ), '[]'::jsonb),
            'definition', pg_catalog.pg_get_functiondef(f.oid)
        ) ORDER BY f.schema_name, f.proname, f.oid)
        FROM relevant_functions AS f
    ), '[]'::jsonb),
    'schema_access', COALESCE((
        SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'schema', n.nspname, 'role', a.rolname, 'bypasses_rls', a.rolbypassrls,
            'usage', pg_catalog.has_schema_privilege(a.oid, n.oid, 'USAGE'),
            'create', pg_catalog.has_schema_privilege(a.oid, n.oid, 'CREATE')
        ) ORDER BY n.nspname, a.rolname)
        FROM pg_catalog.pg_namespace AS n CROSS JOIN application_roles AS a
        WHERE n.nspname IN (SELECT schema_name FROM target_relations)
    ), '[]'::jsonb)
)) AS audit_schema_report;
