CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS "Tenants" (
    "Id" uuid PRIMARY KEY,
    "Slug" varchar(64) NOT NULL UNIQUE,
    "Name" varchar(160) NOT NULL,
    "Domain" varchar(255),
    "ProvisioningStatus" integer NOT NULL DEFAULT 1,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

INSERT INTO "Tenants" ("Id", "Slug", "Name", "Domain", "ProvisioningStatus") VALUES
('11111111-1111-1111-1111-111111111111', 'acme-corp', 'Acme Corporation', 'acme.localhost', 1),
('22222222-2222-2222-2222-222222222222', 'orbit-labs', 'Orbit Labs', 'orbit.localhost', 1)
ON CONFLICT ("Slug") DO NOTHING;

DO $$
DECLARE tenant_id uuid; schema_name text;
BEGIN
  FOR tenant_id IN SELECT "Id" FROM "Tenants" LOOP
    schema_name := 'tenant_' || replace(tenant_id::text, '-', '');
    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', schema_name);
    EXECUTE format('CREATE TABLE IF NOT EXISTS %I."AppUsers" ("Id" uuid PRIMARY KEY, "Email" varchar(320) NOT NULL, "DisplayName" varchar(160) NOT NULL, "IsActive" boolean NOT NULL DEFAULT true)', schema_name);
    EXECUTE format('CREATE TABLE IF NOT EXISTS %I."AuditEntries" ("Id" uuid PRIMARY KEY, "OccurredAt" timestamptz NOT NULL DEFAULT now(), "ActorId" uuid, "Action" varchar(120) NOT NULL, "Resource" varchar(160) NOT NULL, "IpAddress" varchar(64), "TenantId" uuid NOT NULL)', schema_name);
  END LOOP;
END $$;

INSERT INTO "tenant_11111111111111111111111111111111"."AppUsers" ("Id", "Email", "DisplayName") VALUES
(gen_random_uuid(), 'lena@acme.local', 'Lena Morgan'), (gen_random_uuid(), 'jonas@acme.local', 'Jonas Klein') ON CONFLICT DO NOTHING;
INSERT INTO "tenant_22222222222222222222222222222222"."AppUsers" ("Id", "Email", "DisplayName") VALUES
(gen_random_uuid(), 'maya@orbit.local', 'Maya Chen'), (gen_random_uuid(), 'simon@orbit.local', 'Simon Reed') ON CONFLICT DO NOTHING;
