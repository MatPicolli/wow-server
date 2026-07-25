# Gerenciador — interface gráfica

GUI em WPF (.NET 10) para instalar, configurar e operar o servidor sem
linha de comando.

```powershell
cd gui
dotnet run --project WowServer.Gui
```

Ou abra `WowServerManager.sln` no Visual Studio e rode.

---

## O que tem

| Aba | Para quê |
|---|---|
| **Tutorial** | Passo a passo em 11 etapas, em português, explicando o que cada coisa faz e quanto demora. |
| **Instalação** | As 8 etapas da instalação, cada uma executável isoladamente, com console ao lado. |
| **Servidor** | Painéis do authserver e do worldserver, lado a lado ou empilhados, com caixa de comando para os comandos de GM. |
| **Ajustes** | Multiplicadores de coleta e chance de drop (inclusive itens de quest), com perfis prontos e prévia antes de aplicar. |
| **Módulos** | Catálogo com AH Bot, escalonamento de dungeon, LFG solo, Solocraft, Eluna e Playerbots. |
| **⚙ Configurações** | Caminhos, banco de dados, realm e repositório do core — gravados no `settings.psd1`. |

---

## Como está organizado

```
WowServer.Core/        lógica, sem dependência de Windows  (net8.0)
WowServer.Gui/         interface WPF                        (net10.0-windows)
WowServer.Core.Tests/  testes do Core, sem framework externo
```

**A GUI não reimplementa a instalação.** Ela executa os mesmos scripts
PowerShell de `scripts/` e transmite a saída para os painéis. Toda a lógica
difícil — detectar OpenSSL 3.x, aceitar as duas grafias dos extractors,
marcadores de etapa concluída, idempotência dos multiplicadores — continua num
lugar só. Consertar um bug no script conserta na GUI também.

O `Core` é `net8.0` de propósito: assim compila e roda em qualquer plataforma,
e os testes rodam em CI Linux. Para unificar em .NET 10, troque o
`TargetFramework` no `WowServer.Core.csproj`.

### Testes

```powershell
cd gui
dotnet run --project WowServer.Core.Tests
```

Cobrem o que não depende de Windows: edição do `settings.psd1` preservando
comentários e alinhamento, leitura das configurações, montagem dos argumentos
dos scripts e consistência do catálogo.

---

## Detalhes que valem saber

**Elevação.** A GUI roda como usuário comum. Só a instalação de dependências
precisa de administrador, e ela sobe num processo elevado à parte — o Windows
pede confirmação. Como processo elevado não aceita saída redirecionada, essa
etapa abre a própria janela; a GUI mostra só o resultado no fim.

**Comandos de GM.** A caixa embaixo do painel do worldserver escreve na entrada
padrão dele. É por onde vão `account create`, `reload creature_loot_template` e
o resto. Digite sem o ponto.

**Desligar.** O botão "Desligar" pede desligamento limpo, que salva os
personagens. "Forçar encerramento" mata o processo e perde o que não foi
gravado — é para quando travar.

**Playerbots.** Aparece no catálogo com selo de aviso porque não é um módulo
comum: exige substituir o core por um fork. A GUI explica o custo, troca o
repositório no `settings.psd1` e reclona. Os dados extraídos do client e os
personagens sobrevivem.

---

## Limitação conhecida

O projeto WPF **não foi compilado nem executado** — foi escrito num ambiente
Linux, onde o SDK WindowsDesktop não existe. O que foi verificado:

- `WowServer.Core` compila e passa em 40 testes
- todos os `.xaml` são XML válido
- todo `x:Name` usado no code-behind existe no XAML correspondente

Erros de compilação na primeira execução são possíveis. Se aparecerem, são do
tipo direto de resolver (using faltando, nome de propriedade). A lógica que
importa está no `Core`, que está testado.
