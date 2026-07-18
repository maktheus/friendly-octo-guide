using Xunit;

// As classes live compartilham o MESMO Postgres (schema DDL + TRUNCATE): em paralelo,
// o lock de DDL de uma derruba a outra de forma intermitente. Integração roda em série.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
