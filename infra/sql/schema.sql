IF DB_ID('CaixaEscrita') IS NULL CREATE DATABASE CaixaEscrita;
IF DB_ID('CaixaLeitura') IS NULL CREATE DATABASE CaixaLeitura;
IF DB_ID('CaixaIdentidade') IS NULL CREATE DATABASE CaixaIdentidade;
IF SUSER_ID('caixa_write') IS NULL CREATE LOGIN caixa_write WITH PASSWORD='$(WritePassword)';
IF SUSER_ID('caixa_publish') IS NULL CREATE LOGIN caixa_publish WITH PASSWORD='$(PublishPassword)';
IF SUSER_ID('caixa_read') IS NULL CREATE LOGIN caixa_read WITH PASSWORD='$(ReadPassword)';
IF SUSER_ID('caixa_project') IS NULL CREATE LOGIN caixa_project WITH PASSWORD='$(ProjectPassword)';
IF SUSER_ID('caixa_identity') IS NULL CREATE LOGIN caixa_identity WITH PASSWORD='$(IdentityPassword)';
GO
USE CaixaEscrita;
GO
IF OBJECT_ID('dbo.Lancamento') IS NULL
BEGIN
    CREATE TABLE dbo.Lancamento(
        Id uniqueidentifier NOT NULL PRIMARY KEY, ContaId uniqueidentifier NOT NULL, DataCaixa date NOT NULL,
        Tipo varchar(7) NOT NULL CHECK(Tipo IN ('credito','debito')), Valor decimal(19,2) NOT NULL CHECK(Valor>0),
        Descricao nvarchar(200) NULL, CriadoEmUtc datetimeoffset NOT NULL, Chave uniqueidentifier NOT NULL,
        Hash binary(32) NOT NULL, CONSTRAINT UQ_Lancamento_Chave UNIQUE(ContaId,Chave)
    );
    CREATE INDEX IX_Lancamento_Historico ON dbo.Lancamento(ContaId,DataCaixa DESC,Id DESC);
END;
IF OBJECT_ID('dbo.Outbox') IS NULL
BEGIN
    CREATE TABLE dbo.Outbox(
        EventoId uniqueidentifier NOT NULL PRIMARY KEY, LancamentoId uniqueidentifier NOT NULL UNIQUE REFERENCES dbo.Lancamento(Id),
        Envelope nvarchar(max) NOT NULL, TraceParent varchar(128) NULL, TraceState nvarchar(512) NULL,
        CriadoEmUtc datetimeoffset NOT NULL, ProximaTentativa datetimeoffset NOT NULL,
        Tentativas int NOT NULL DEFAULT 0, PublicadoEmUtc datetimeoffset NULL
    );
END;
SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Outbox') AND name='IX_Outbox_Pendente')
    CREATE INDEX IX_Outbox_Pendente ON dbo.Outbox(ProximaTentativa,EventoId) WHERE PublicadoEmUtc IS NULL;
IF USER_ID('caixa_write') IS NULL CREATE USER caixa_write FROM LOGIN caixa_write;
IF USER_ID('caixa_publish') IS NULL CREATE USER caixa_publish FROM LOGIN caixa_publish;
GRANT SELECT, INSERT ON dbo.Lancamento TO caixa_write;
GRANT INSERT ON dbo.Outbox TO caixa_write;
GRANT SELECT, UPDATE ON dbo.Outbox TO caixa_publish;
GO
USE CaixaLeitura;
GO
IF OBJECT_ID('dbo.ResumoDiario') IS NULL
    CREATE TABLE dbo.ResumoDiario(
        ContaId uniqueidentifier NOT NULL, DataCaixa date NOT NULL, Creditos decimal(27,2) NOT NULL,
        Debitos decimal(27,2) NOT NULL, Quantidade bigint NOT NULL, AtualizadoEmUtc datetimeoffset NOT NULL,
        CONSTRAINT PK_Resumo PRIMARY KEY(ContaId,DataCaixa)
    );
IF OBJECT_ID('dbo.EventoAplicado') IS NULL
    CREATE TABLE dbo.EventoAplicado(
        EventoId uniqueidentifier NOT NULL PRIMARY KEY, ContaId uniqueidentifier NOT NULL,
        DataCaixa date NOT NULL, AplicadoEmUtc datetimeoffset NOT NULL
    );
IF USER_ID('caixa_read') IS NULL CREATE USER caixa_read FROM LOGIN caixa_read;
IF USER_ID('caixa_project') IS NULL CREATE USER caixa_project FROM LOGIN caixa_project;
GRANT SELECT ON dbo.ResumoDiario TO caixa_read;
GRANT SELECT, INSERT, UPDATE ON dbo.ResumoDiario TO caixa_project;
GRANT SELECT, INSERT ON dbo.EventoAplicado TO caixa_project;
GO
USE CaixaIdentidade;
GO
IF USER_ID('caixa_identity') IS NULL
BEGIN
    CREATE USER caixa_identity FROM LOGIN caixa_identity;
    ALTER ROLE db_owner ADD MEMBER caixa_identity;
END;
GO
