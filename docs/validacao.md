# Validação executada · 02/11/2026

- `dotnet build src/RetryLab.csproj`: sucesso, zero erros e zero avisos.
- `dotnet test tests/RetryLab.Tests.csproj --configuration Release`: 12 aprovados, zero falhas, zero ignorados; duração reportada de 2s. É duração desta execução, não benchmark.
- `python scripts/check_http.py`: 12 requisições paralelas da mesma operação resultaram em uma criação e 11 replays; conflito de payload 409; falta de chave 400; Origin externo 403; modo inválido 400.
- Navegador real: criação de operação, simulação de confirmação perdida e entrega posterior; o evento `9c2660d551d7...` passou a entregue na segunda tentativa e não ganhou outro efeito no receptor.
- Capturas reais em `screenshots/`: estado com falha, recuperação e mobile.
- Tela pequena: viewport de 390px, sem overflow horizontal da página; a tabela tem área própria de rolagem. Botões principais com cerca de 298px de largura. Viewport restaurado depois da conferência.

Os dados exibidos nas capturas são fictícios, criados na verificação. Não foi testado tráfego real, integração com empresas ou execução em cluster. O modelo de CI está em `github-actions-ci.example.yml`, ainda desativado: a credencial GitHub disponível não permite publicar workflows. Não foram solicitadas permissões adicionais. Esta nota só afirma os testes locais, não um resultado de CI remoto.
