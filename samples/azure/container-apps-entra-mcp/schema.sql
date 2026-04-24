IF OBJECT_ID(N'dbo.Todos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Todos
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Todos PRIMARY KEY,
        Title NVARCHAR(200) NOT NULL,
        OwnerNote NVARCHAR(200) NULL,
        IsComplete BIT NOT NULL CONSTRAINT DF_Todos_IsComplete DEFAULT 0,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Todos_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Todos)
BEGIN
    INSERT INTO dbo.Todos (Title, OwnerNote, IsComplete)
    VALUES
        (N'Confirm DAB REST authentication', N'Seed row for REST and GraphQL validation', 0),
        (N'Confirm DAB MCP tools', N'Seed row for MCP inspector or agent testing', 0),
        (N'Explain OBO to customer', N'The database sees the delegated user token', 1);
END;
