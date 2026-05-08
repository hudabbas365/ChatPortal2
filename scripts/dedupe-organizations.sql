/*
    dedupe-organizations.sql

    Removes duplicate rows from dbo.Organizations so the unique index
    IX_Organizations_Name_Unique (added by migration AddUniqueOrganizationName)
    can be created.

    Strategy:
      1. For each Name, keep the row with the smallest Id (oldest).
      2. Repoint every FK that references Organizations.Id from the
         duplicates to the kept row.
      3. Delete the duplicate rows.

    Run inside a transaction so you can ROLLBACK if anything looks wrong.
*/
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1. Build the keep / drop map.
IF OBJECT_ID('tempdb..#OrgMap') IS NOT NULL DROP TABLE #OrgMap;

;WITH ranked AS (
    SELECT Id,
           Name,
           ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Id) AS rn,
           MIN(Id)      OVER (PARTITION BY Name)             AS KeepId
    FROM dbo.Organizations
)
SELECT Id AS DuplicateId, KeepId, Name
INTO   #OrgMap
FROM   ranked
WHERE  rn > 1;

SELECT 'Duplicates to merge' AS Info, COUNT(*) AS Cnt FROM #OrgMap;
SELECT * FROM #OrgMap ORDER BY Name, DuplicateId;

-- 2. Repoint every child table.
UPDATE u  SET u.OrganizationId  = m.KeepId
FROM dbo.AspNetUsers u    JOIN #OrgMap m ON u.OrganizationId  = m.DuplicateId;

UPDATE w  SET w.OrganizationId  = m.KeepId
FROM dbo.Workspaces w     JOIN #OrgMap m ON w.OrganizationId  = m.DuplicateId;

UPDATE a  SET a.OrganizationId  = m.KeepId
FROM dbo.ActivityLogs a   JOIN #OrgMap m ON a.OrganizationId  = m.DuplicateId;

UPDATE a  SET a.OrganizationId  = m.KeepId
FROM dbo.Agents a         JOIN #OrgMap m ON a.OrganizationId  = m.DuplicateId;

UPDATE d  SET d.OrganizationId  = m.KeepId
FROM dbo.Datasources d    JOIN #OrgMap m ON d.OrganizationId  = m.DuplicateId;

UPDATE n  SET n.OrganizationId  = m.KeepId
FROM dbo.Notifications n  JOIN #OrgMap m ON n.OrganizationId  = m.DuplicateId;

UPDATE p  SET p.OrganizationId  = m.KeepId
FROM dbo.PaymentRecords p JOIN #OrgMap m ON p.OrganizationId  = m.DuplicateId;

UPDATE p  SET p.OrganizationId  = m.KeepId
FROM dbo.PlanChangeLogs p JOIN #OrgMap m ON p.OrganizationId  = m.DuplicateId;

UPDATE t  SET t.OrganizationId  = m.KeepId
FROM dbo.SupportTickets t JOIN #OrgMap m ON t.OrganizationId  = m.DuplicateId;

UPDATE t  SET t.OrganizationId  = m.KeepId
FROM dbo.TokenUsages t    JOIN #OrgMap m ON t.OrganizationId  = m.DuplicateId;

-- 3. Delete the duplicate org rows.
DELETE o
FROM dbo.Organizations o
JOIN #OrgMap m ON o.Id = m.DuplicateId;

-- 4. Verify no duplicate names remain.
SELECT Name, COUNT(*) AS Cnt
FROM dbo.Organizations
GROUP BY Name
HAVING COUNT(*) > 1;
-- ^ should return zero rows.

-- If the above looks correct, COMMIT. Otherwise ROLLBACK.
COMMIT;
-- ROLLBACK;
