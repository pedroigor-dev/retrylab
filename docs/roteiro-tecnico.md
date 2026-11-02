# Como apresentar sem decorar uma resposta

1. Abra `Store.Create`: por que operação e evento entram na mesma transação? O teste com trigger demonstra qual falha?
2. Diferencie chave de idempotência e ID de evento. Quem cria cada um? Por que o ID não muda no reprocessamento?
3. Abra `Claim`: que problema `BEGIN IMMEDIATE` evita? Por que SQLite não prova que o mesmo desenho escala em PostgreSQL?
4. Simule `AcknowledgementLost`: por que um timeout não autoriza criar uma operação nova?
5. Abra `Finish`: como o token impede que um worker antigo sobrescreva o estado? Qual efeito ele não impede?
6. Explique a diferença entre entrega repetida e efeito único. O receptor precisa participar da garantia.
7. Mostre backoff, fila de falhas e reabertura. Discuta quando um erro deve ir direto para falha definitiva.
8. Reinicie a aplicação: os bancos preservam os estados. O teste do lease usa relógio controlado, não espera real.
9. Explique os limites: sem rede entre componentes, sem autenticação, sem carga de produção, sem dados reais.
10. Para produção, proponha um passo de cada vez: receptor HTTP com contrato idempotente, autenticação, métricas, banco adequado e worker hospedado. Só então compare comportamento e carga medidos.

Se não conseguir explicar uma parte, use o laboratório para estudá-la antes da entrevista. Código criado com auxílio de ferramentas deve ser apresentado como projeto pessoal assistido, sem atribuir uso profissional ou resultado empresarial que não ocorreu.
