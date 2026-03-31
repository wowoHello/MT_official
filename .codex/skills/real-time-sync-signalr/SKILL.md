---
name: real-time-sync-signalr
description: 提供 Blazor 應用程式中實現應用程式層級即時資料同步的指南，利用 ASP.NET Core SignalR 在資料庫寫入操作後，主動將資料變更推播至所有前端客戶端，實現免重新整理的高互動性使用者畫面。適用於需要建立即時更新、高效能 Blazor 應用程式的開發任務。
---

# 應用程式層級即時資料同步 (Application-Level Real-Time Sync)

## 🎯 技能目的 (Objective)

在不增加資料庫額外輪詢 (Polling) 負擔的前提下，當應用程式執行資料庫異動 (新增、修改、刪除) 時，主動且即時地將變更推播至所有前端客戶端，實現「免重新整理 (No-F5)」的高互動性使用者畫面。

## 🛠️ 技術依賴 (Tech Stack)

- **前端框架**： Blazor (.NET 10)
- **即時通訊**： ASP.NET Core SignalR
- **資料存取**： Dapper
- **資料庫**： SSMS（MYSQL語法）

## ⚙️ 觸發條件 (Trigger Conditions)

本技能僅在**「由當前 .NET 應用程式主動發起對資料庫的寫入操作 (Write Operations)」**時觸發。若外部系統或 DBA 直接修改底層資料庫，則不會觸發此技能。

## 🚀 執行標準流程 (Execution Protocol)

### 步驟一：定義通訊中繼站 (Hub)

建立一個繼承自 `Microsoft.AspNetCore.SignalR.Hub` 的類別，作為伺服器與所有客戶端之間的通訊通道。實作重點在於保持 Hub 輕量，主要業務邏輯應放在 Service 層。

```csharp
// Example: MyHub.cs
using Microsoft.AspNetCore.SignalR;

namespace YourProject.Hubs
{
    public class MyHub : Hub
    {
        // Hub methods can be added here if needed, but for simple broadcasting,
        // direct injection of IHubContext in services is often preferred.
    }
}
```

### 步驟二：封裝 Service 層寫入與廣播邏輯

在負責處理業務邏輯的 Service 中，注入 `DbContext` (或 Dapper 的 `IDbConnection`) 與 `IHubContext<T>`。必須確保資料庫交易成功後，才進行廣播。

- **新增 (Create)**： 執行 `SaveChangesAsync()` (或 Dapper 的 `ExecuteAsync()`) 後，將新物件廣播。
- **修改 (Update)**： 執行 `SaveChangesAsync()` (或 Dapper 的 `ExecuteAsync()`) 後，將更新後的物件廣播。
- **刪除 (Delete)**： 執行 `SaveChangesAsync()` (或 Dapper 的 `ExecuteAsync()`) 後，將被刪除資料的 唯一識別碼 (ID) 廣播。

```csharp
// Example: MyService.cs
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Hubs;
using YourProject.Models;

namespace YourProject.Services
{
    public class MyService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<MyHub> _hubContext;

        public MyService(ApplicationDbContext context, IHubContext<MyHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task AddItemAsync(Item item)
        {
            _context.Items.Add(item);
            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ReceiveAddItem", item);
        }

        public async Task UpdateItemAsync(Item item)
        {
            _context.Entry(item).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ReceiveUpdateItem", item);
        }

        public async Task DeleteItemAsync(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item != null)
            {
                _context.Items.Remove(item);
                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ReceiveDeleteItem", id);
            }
        }
    }
}
```

### 步驟三：前端初始化與訂閱 (Client Subscription)

在 Blazor 頁面的 `OnInitializedAsync` 生命週期中：

- 發起 HTTP/SQL 請求，獲取當下的初始資料列表。
- 建立 `HubConnection` 並啟動 WebSocket 連線。
- 針對不同的廣播事件註冊監聽器 (Listeners)。

```razor
@page "/realtimeitems"
@using Microsoft.AspNetCore.SignalR.Client
@inject HttpClient HttpClient
@implements IAsyncDisposable

<h3>即時項目列表</h3>

@if (items == null)
{
    <p><em>載入中...</em></p>
}
else
{
    <ul>
        @foreach (var item in items)
        {
            <li>@item.Name (ID: @item.Id)</li>
        }
    </ul>
}

@code {
    private HubConnection? hubConnection;
    private List<Item>? items;

    protected override async Task OnInitializedAsync()
    {
        // 1. 獲取初始資料
        items = await HttpClient.GetFromJsonAsync<List<Item>>("/api/items");

        // 2. 建立 HubConnection 並啟動
        hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/myhub"))
            .Build();

        // 3. 註冊監聽器
        hubConnection.On<Item>("ReceiveAddItem", (item) =>
        {
            items?.Insert(0, item);
            InvokeAsync(StateHasChanged);
        });

        hubConnection.On<Item>("ReceiveUpdateItem", (updatedItem) =>
        {
            var existingItem = items?.FirstOrDefault(i => i.Id == updatedItem.Id);
            if (existingItem != null)
            {
                existingItem.Name = updatedItem.Name;
                InvokeAsync(StateHasChanged);
            }
        });

        hubConnection.On<int>("ReceiveDeleteItem", (id) =>
        {
            var itemToRemove = items?.FirstOrDefault(i => i.Id == id);
            if (itemToRemove != null)
            {
                items?.Remove(itemToRemove);
                InvokeAsync(StateHasChanged);
            }
        });

        await hubConnection.StartAsync();
    }

    // 4. 連線生命週期管理
    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }

    public class Item // Example Model
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
```

### 步驟四：前端記憶體資料同步與 UI 渲染

當前端接收到 SignalR 推播時，直接操作記憶體中的資料集合 (`List/Collection`)，而不重新向資料庫發起查詢：

- **接收新增**： `List.Insert(0, newObject)`
- **接收修改**： 根據 ID 找到索引，`List[index] = updatedObject` (或直接更新物件屬性)
- **接收刪除**： 根據 ID 找到物件，`List.Remove(targetObject)`
- **強制渲染**： 執行完集合操作後，呼叫 `InvokeAsync(StateHasChanged)` 更新畫面。

## 💡 資料庫專家架構守則 (Expert Constraints & Best Practices)

- **資料庫解耦**： 此架構的優勢在於資料庫不需要知道 SignalR 的存在，資料庫的 CPU 與 I/O 資源 100% 保留給核心的 CRUD 操作。

- **ID 穩定性**： 資料表必須具備不可變的 Primary Key (如自動遞增 INT 或 UUID)，這是前端在記憶體中進行修改與刪除的唯一對比基準。

- **橫向擴展 (Scale-Out) 警告**： 此為單機版 (Single-Server) 實作。若未來系統擴展為多台伺服器負載平衡，必須引入 Redis 擔任 SignalR Backplane (背板)，否則各伺服器之間的即時推播將無法互通。詳細資訊請參考 `references/signalr_backplane.md`。

- **連線生命週期管理**： 務必在 Blazor 頁面實作 `IAsyncDisposable` 介面，在使用者離開頁面時正確釋放 `HubConnection`，避免伺服器發生記憶體洩漏 (Memory Leak)。

## 資源

此技能包含以下資源，可作為進一步參考：

### references/

- `signalr_backplane.md`: SignalR 橫向擴展與 Redis Backplane 的詳細實作指南。

### templates/

- `hub_template.txt`: SignalR Hub 類別的範本。
- `service_realtime_template.txt`: 整合 SignalR 廣播邏輯的 Service 層範本。
- `blazor_page_realtime_template.txt`: 包含 SignalR 客戶端訂閱與資料同步邏輯的 Blazor 頁面範本。

---

**注意**：此技能的 `scripts/` 目錄目前沒有包含任何可執行腳本，因為即時同步主要涉及程式碼撰寫和配置指南。如有需要，未來可根據具體需求添加自動化腳本。
