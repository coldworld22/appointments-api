-- Demo branches and services. Applied only when Database:SeedDemoData is true (local runs, tests, the demo deployment).

SET IDENTITY_INSERT dbo.Branches ON;
INSERT INTO dbo.Branches (Id, Name, City) VALUES
    (1, N'Abha Main Service Centre', N'Abha'),
    (2, N'Khamis Mushait Service Centre', N'Khamis Mushait'),
    (3, N'Riyadh Olaya Service Centre', N'Riyadh');
SET IDENTITY_INSERT dbo.Branches OFF;

SET IDENTITY_INSERT dbo.ServiceTypes ON;
INSERT INTO dbo.ServiceTypes (Id, Code, Name, DurationMinutes) VALUES
    (1, N'ID_RENEWAL', N'National ID renewal', 15),
    (2, N'VEHICLE_REGISTRATION', N'Vehicle registration', 30),
    (3, N'BUILDING_PERMIT_REVIEW', N'Building permit review', 45);
SET IDENTITY_INSERT dbo.ServiceTypes OFF;
