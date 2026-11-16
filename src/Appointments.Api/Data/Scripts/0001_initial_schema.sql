-- Service centres, the services they offer, bookable time slots and citizens' appointments.
-- Times are datetimeoffset so they compare by instant whatever offset they were written with.

CREATE TABLE dbo.Branches (
    Id    INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
    Name  NVARCHAR(100)      NOT NULL,
    City  NVARCHAR(100)      NOT NULL
);

CREATE TABLE dbo.ServiceTypes (
    Id               INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ServiceTypes PRIMARY KEY,
    Code             NVARCHAR(50)       NOT NULL CONSTRAINT UQ_ServiceTypes_Code UNIQUE,
    Name             NVARCHAR(100)      NOT NULL,
    DurationMinutes  INT                NOT NULL CONSTRAINT CK_ServiceTypes_Duration CHECK (DurationMinutes BETWEEN 5 AND 240)
);

CREATE TABLE dbo.Slots (
    Id             BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Slots PRIMARY KEY,
    BranchId       INT                   NOT NULL CONSTRAINT FK_Slots_Branches REFERENCES dbo.Branches (Id),
    ServiceTypeId  INT                   NOT NULL CONSTRAINT FK_Slots_ServiceTypes REFERENCES dbo.ServiceTypes (Id),
    StartsAt       DATETIMEOFFSET        NOT NULL,
    EndsAt         DATETIMEOFFSET        NOT NULL,
    Capacity       INT                   NOT NULL,
    Booked         INT                   NOT NULL CONSTRAINT DF_Slots_Booked DEFAULT 0,
    RowVersion     ROWVERSION            NOT NULL,
    CONSTRAINT UQ_Slots_Branch_Service_Start UNIQUE (BranchId, ServiceTypeId, StartsAt),
    CONSTRAINT CK_Slots_Capacity CHECK (Capacity BETWEEN 1 AND 50),
    -- Last line of defence: even a bug in the application can't overbook a slot.
    CONSTRAINT CK_Slots_Booked CHECK (Booked >= 0 AND Booked <= Capacity),
    CONSTRAINT CK_Slots_Times CHECK (EndsAt > StartsAt)
);

CREATE TABLE dbo.Appointments (
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Appointments PRIMARY KEY,
    Reference      NVARCHAR(12)     NOT NULL CONSTRAINT UQ_Appointments_Reference UNIQUE,
    SlotId         BIGINT           NOT NULL CONSTRAINT FK_Appointments_Slots REFERENCES dbo.Slots (Id),
    BranchId       INT              NOT NULL,
    ServiceTypeId  INT              NOT NULL,
    CitizenId      NVARCHAR(64)     NOT NULL,
    StartsAt       DATETIMEOFFSET   NOT NULL,
    Status         INT              NOT NULL,
    CreatedAt      DATETIMEOFFSET   NOT NULL,
    CancelledAt    DATETIMEOFFSET   NULL,
    CheckedInAt    DATETIMEOFFSET   NULL,
    CheckedInBy    NVARCHAR(64)     NULL,
    CompletedAt    DATETIMEOFFSET   NULL,
    CONSTRAINT CK_Appointments_Status CHECK (Status BETWEEN 0 AND 3)
);

-- "Does this citizen already hold an active booking for this service?" (checked on every booking)
CREATE INDEX IX_Appointments_Citizen ON dbo.Appointments (CitizenId, ServiceTypeId, Status) INCLUDE (StartsAt);

-- Counter staff's queue for a branch and day.
CREATE INDEX IX_Appointments_Branch_StartsAt ON dbo.Appointments (BranchId, StartsAt) INCLUDE (Status, Reference);

-- Open slots for a branch, service and day.
CREATE INDEX IX_Slots_Search ON dbo.Slots (BranchId, ServiceTypeId, StartsAt) INCLUDE (Capacity, Booked, EndsAt);
