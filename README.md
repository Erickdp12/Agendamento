# Sistema de Agendamentos

API REST em C# com ASP.NET Core 10, SQLite e autenticação JWT. O projeto usa uma agenda compartilhada e um operador configurado na instalação. Os clientes são cadastros de contato.

## Executar localmente

Instale o **SDK do .NET 10** e abra o terminal na pasta `SistemaAgendamentos`. Os comandos abaixo usam PowerShell.

```powershell
$chaveAgenda = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Agenda:Usuario" "admin"
dotnet user-secrets set "Agenda:Senha" "SUBSTITUA_POR_UMA_SENHA_LONGA"
dotnet user-secrets set "Agenda:ChaveJwt" $chaveAgenda

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run -- --urls http://localhost:5080
```

Escolha uma senha própria de pelo menos 12 caracteres. SQLite cria `App_Data/agenda.sqlite3` no primeiro uso. Também é possível configurar o serviço pelas variáveis `Agenda__Usuario`, `Agenda__Senha` e `Agenda__ChaveJwt`. O endereço HTTP acima é para uso local. Para exposição em rede, configure HTTPS e um provedor de identidade OIDC.

## Rotas

O token expira em uma hora. Após o login, envie `Authorization: Bearer SEU_TOKEN` nas rotas protegidas.

| Método | Rota | Operação |
| --- | --- | --- |
| POST | `/api/login` | Gera o JWT |
| POST | `/api/clientes` | Cadastra um cliente |
| GET | `/api/clientes` | Lista clientes |
| GET | `/api/clientes/{id}` | Consulta um cliente |
| PUT | `/api/clientes/{id}` | Atualiza nome, e-mail e telefone |
| POST | `/api/agendamentos` | Reserva um horário |
| GET | `/api/agendamentos` | Lista reservas ativas e canceladas, com filtro opcional `de` e `ate` |
| GET | `/api/agendamentos/{id}` | Consulta uma reserva |
| PUT | `/api/agendamentos/{id}/horario` | Altera início e fim de uma reserva ativa |
| PUT | `/api/agendamentos/{id}/cancelamento` | Cancela uma reserva e libera o horário |

Corpos JSON, na ordem de uso:

```json
{"usuario":"admin","senha":"SUA_SENHA"}
```

```json
{"nome":"Ana Souza","email":"ana@example.com","telefone":"55 11 99999-0000"}
```

```json
{"clienteId":1,"inicio":"2030-11-20T14:00:00-03:00","fim":"2030-11-20T15:00:00-03:00"}
```

Use no agendamento o `id` devolvido pelo cadastro do cliente. Para reagendar, envie somente `inicio` e `fim`. O cancelamento não recebe corpo.

## Regras de horário

- Envie datas ISO 8601 com fuso explícito. A API devolve os horários em UTC. Parâmetros de URL que tragam `+` no fuso devem usar `%2B`.
- Reservas novas e reagendamentos devem começar no futuro. O fim deve ser posterior ao início.
- Uma reserva das 14h às 15h bloqueia qualquer intervalo sobreposto. Outra pode começar exatamente às 15h.
- O próprio SQLite aplica a regra na gravação. Duas requisições concorrentes para o mesmo horário produzem uma reserva e uma resposta `409 Conflict`, desde que o banco esteja disponível.
- O cancelamento guarda a data original e libera o horário. Repetir o cancelamento preserva essa data.
- A listagem com `de` e `ate` inclui todas as reservas que cruzam o período solicitado, inclusive canceladas.

## Verificar pela API

Com a API rodando em um banco de teste, execute o roteiro abaixo em outro terminal com Python 3. O roteiro cria um cliente fictício e deixa registros históricos no banco.

```powershell
python teste_api.py
```

O roteiro pede o usuário e a senha sem exibir a senha e verifica autenticação, concorrência, conflito no reagendamento, horários consecutivos, cancelamento e liberação do horário.
