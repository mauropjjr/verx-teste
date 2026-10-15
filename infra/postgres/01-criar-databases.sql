-- Executado uma única vez pelo entrypoint do Postgres, na criação do volume.
-- Cada serviço tem seu próprio database (design D1): sem acesso cruzado a tabelas.
CREATE DATABASE identidade;
CREATE DATABASE lancamentos;
CREATE DATABASE consolidado;
