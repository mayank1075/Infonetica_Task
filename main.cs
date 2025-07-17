using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<IWorkflowService, WorkflowService>();
builder.Services.AddSingleton<IWorkflowStorage, InMemoryWorkflowStorage>();
builder.Services.Configure<JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseRouting();

// Workflow Definition endpoints
app.MapPost("/api/workflow-definitions", async (CreateWorkflowDefinitionRequest request, IWorkflowService service) => {
    try{
        var definition = await service.CreateWorkflowDefinitionAsync(request);
        return Results.Created($"/api/workflow-definitions/{definition.Id}", definition);
    }catch(ValidationException ex){
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/workflow-definitions/{id}", async (string id, IWorkflowService service) =>{
    var definition = await service.GetWorkflowDefinitionAsync(id);
    return definition != null ? Results.Ok(definition) : Results.NotFound();
});

app.MapGet("/api/workflow-definitions", async (IWorkflowService service) => {
    var definitions = await service.GetAllWorkflowDefinitionsAsync();
    return Results.Ok(definitions);
});

// Workflow Instance endpoints
app.MapPost("/api/workflow-instances", async (CreateWorkflowInstanceRequest request, IWorkflowService service) => {
    try{
        var instance = await service.CreateWorkflowInstanceAsync(request.DefinitionId);
        return Results.Created($"/api/workflow-instances/{instance.Id}", instance);
    }catch(ValidationException ex){
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/workflow-instances/{id}", async (string id, IWorkflowService service) => {
    var instance = await service.GetWorkflowInstanceAsync(id);
    return instance != null ? Results.Ok(instance) : Results.NotFound();
});

app.MapGet("/api/workflow-instances", async (IWorkflowService service) => {
    var instances = await service.GetAllWorkflowInstancesAsync();
    return Results.Ok(instances);
});

app.MapPost("/api/workflow-instances/{id}/execute", async (string id, ExecuteActionRequest request, IWorkflowService service) => {
    try{
        var instance = await service.ExecuteActionAsync(id, request.ActionId);
        return Results.Ok(instance);
    }catch(ValidationException ex){
        return Results.BadRequest(new { error = ex.Message });
    }catch(InvalidOperationException ex){
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

// Models
public class WorkflowState{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsInitial { get; set; }
    public bool IsFinal { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Description { get; set; }
}

public class WorkflowAction{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> FromStates { get; set; } = new();
    public string ToState { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class WorkflowDefinition{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<WorkflowState> States { get; set; } = new();
    public List<WorkflowAction> Actions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
}

public class WorkflowInstance{
    public string Id { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string CurrentStateId { get; set; } = string.Empty;
    public List<WorkflowHistoryEntry> History { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class WorkflowHistoryEntry{
    public string ActionId { get; set; } = string.Empty;
    public string FromStateId { get; set; } = string.Empty;
    public string ToStateId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Request/Response DTOs
public class CreateWorkflowDefinitionRequest{
    public string Name { get; set; } = string.Empty;
    public List<WorkflowState> States { get; set; } = new();
    public List<WorkflowAction> Actions { get; set; } = new();
    public string? Description { get; set; }
}

public class CreateWorkflowInstanceRequest{
    public string DefinitionId { get; set; } = string.Empty;
}

public class ExecuteActionRequest{
    public string ActionId { get; set; } = string.Empty;
}

// Exceptions
public class ValidationException : Exception{
    public ValidationException(string message) : base(message) { }
}

// Interfaces
public interface IWorkflowStorage{
    Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(string id);
    Task<List<WorkflowDefinition>> GetAllWorkflowDefinitionsAsync();
    Task SaveWorkflowDefinitionAsync(WorkflowDefinition definition);
    Task<WorkflowInstance?> GetWorkflowInstanceAsync(string id);
    Task<List<WorkflowInstance>> GetAllWorkflowInstancesAsync();
    Task SaveWorkflowInstanceAsync(WorkflowInstance instance);
}

public interface IWorkflowService{
    Task<WorkflowDefinition> CreateWorkflowDefinitionAsync(CreateWorkflowDefinitionRequest request);
    Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(string id);
    Task<List<WorkflowDefinition>> GetAllWorkflowDefinitionsAsync();
    Task<WorkflowInstance> CreateWorkflowInstanceAsync(string definitionId);
    Task<WorkflowInstance?> GetWorkflowInstanceAsync(string id);
    Task<List<WorkflowInstance>> GetAllWorkflowInstancesAsync();
    Task<WorkflowInstance> ExecuteActionAsync(string instanceId, string actionId);
}

// Storage Implementation
public class InMemoryWorkflowStorage : IWorkflowStorage{
    private readonly Dictionary<string, WorkflowDefinition> _definitions = new();
    private readonly Dictionary<string, WorkflowInstance> _instances = new();
    private readonly object _lock = new();

    public Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(string id){
        lock (_lock){
            _definitions.TryGetValue(id, out var definition);
            return Task.FromResult(definition);
        }
    }

    public Task<List<WorkflowDefinition>> GetAllWorkflowDefinitionsAsync(){
        lock (_lock){
            return Task.FromResult(_definitions.Values.ToList());
        }
    }

    public Task SaveWorkflowDefinitionAsync(WorkflowDefinition definition){
        lock (_lock){
            _definitions[definition.Id] = definition;
            return Task.CompletedTask;
        }
    }

    public Task<WorkflowInstance?> GetWorkflowInstanceAsync(string id){
        lock (_lock){
            _instances.TryGetValue(id, out var instance);
            return Task.FromResult(instance);
        }
    }

    public Task<List<WorkflowInstance>> GetAllWorkflowInstancesAsync(){
        lock (_lock){
            return Task.FromResult(_instances.Values.ToList());
        }
    }

    public Task SaveWorkflowInstanceAsync(WorkflowInstance instance){
        lock (_lock){
            _instances[instance.Id] = instance;
            return Task.CompletedTask;
        }
    }
}

// Service Implementation
public class WorkflowService : IWorkflowService{
    private readonly IWorkflowStorage _storage;

    public WorkflowService(IWorkflowStorage storage){
        _storage = storage;
    }

    public async Task<WorkflowDefinition> CreateWorkflowDefinitionAsync(CreateWorkflowDefinitionRequest request){
        // Validate the workflow definition
        ValidateWorkflowDefinition(request);

        var definition = new WorkflowDefinition{
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            States = request.States,
            Actions = request.Actions,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        await _storage.SaveWorkflowDefinitionAsync(definition);
        return definition;
    }

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(string id){
        return await _storage.GetWorkflowDefinitionAsync(id);
    }

    public async Task<List<WorkflowDefinition>> GetAllWorkflowDefinitionsAsync(){
        return await _storage.GetAllWorkflowDefinitionsAsync();
    }

    public async Task<WorkflowInstance> CreateWorkflowInstanceAsync(string definitionId){
        var definition = await _storage.GetWorkflowDefinitionAsync(definitionId);
        if (definition == null){
            throw new ValidationException($"Workflow definition with ID '{definitionId}' not found");
        }

        var initialState = definition.States.FirstOrDefault(s => s.IsInitial);
        if (initialState == null){
            throw new ValidationException("Workflow definition must have exactly one initial state");
        }

        var instance = new WorkflowInstance{
            Id = Guid.NewGuid().ToString(),
            DefinitionId = definitionId,
            CurrentStateId = initialState.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _storage.SaveWorkflowInstanceAsync(instance);
        return instance;
    }

    public async Task<WorkflowInstance?> GetWorkflowInstanceAsync(string id){
        return await _storage.GetWorkflowInstanceAsync(id);
    }

    public async Task<List<WorkflowInstance>> GetAllWorkflowInstancesAsync(){
        return await _storage.GetAllWorkflowInstancesAsync();
    }

    public async Task<WorkflowInstance> ExecuteActionAsync(string instanceId, string actionId){
        var instance = await _storage.GetWorkflowInstanceAsync(instanceId);
        if (instance == null){
            throw new ValidationException($"Workflow instance with ID '{instanceId}' not found");
        }

        var definition = await _storage.GetWorkflowDefinitionAsync(instance.DefinitionId);
        if (definition == null){
            throw new InvalidOperationException($"Workflow definition with ID '{instance.DefinitionId}' not found");
        }

        var currentState = definition.States.FirstOrDefault(s => s.Id == instance.CurrentStateId);
        if (currentState == null){
            throw new InvalidOperationException($"Current state '{instance.CurrentStateId}' not found in definition");
        }

        if (currentState.IsFinal){
            throw new ValidationException("Cannot execute actions on instances in final states");
        }

        var action = definition.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null){
            throw new ValidationException($"Action with ID '{actionId}' not found in workflow definition");
        }

        if (!action.Enabled){
            throw new ValidationException($"Action '{actionId}' is disabled");
        }

        if (!action.FromStates.Contains(instance.CurrentStateId)){
            throw new ValidationException($"Action '{actionId}' cannot be executed from current state '{instance.CurrentStateId}'");
        }

        var targetState = definition.States.FirstOrDefault(s => s.Id == action.ToState);
        if (targetState == null){
            throw new ValidationException($"Target state '{action.ToState}' not found in workflow definition");
        }

        if (!targetState.Enabled){
            throw new ValidationException($"Target state '{action.ToState}' is disabled");
        }

        // Execute the action
        var historyEntry = new WorkflowHistoryEntry{
            ActionId = actionId,
            FromStateId = instance.CurrentStateId,
            ToStateId = action.ToState,
            Timestamp = DateTime.UtcNow
        };

        instance.History.Add(historyEntry);
        instance.CurrentStateId = action.ToState;
        instance.UpdatedAt = DateTime.UtcNow;

        await _storage.SaveWorkflowInstanceAsync(instance);
        return instance;
    }

    private static void ValidateWorkflowDefinition(CreateWorkflowDefinitionRequest request){
        if (string.IsNullOrWhiteSpace(request.Name)){
            throw new ValidationException("Workflow name is required");
        }

        if (request.States == null || request.States.Count == 0){
            throw new ValidationException("Workflow must have at least one state");
        }

        if (request.Actions == null){
            request.Actions = new List<WorkflowAction>();
        }

        // Check for duplicate state IDs
        var stateIds = request.States.Select(s => s.Id).ToList();
        var duplicateStateIds = stateIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);
        if (duplicateStateIds.Any()){
            throw new ValidationException($"Duplicate state IDs found: {string.Join(", ", duplicateStateIds)}");
        }

        // Check for duplicate action IDs
        var actionIds = request.Actions.Select(a => a.Id).ToList();
        var duplicateActionIds = actionIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);
        if (duplicateActionIds.Any()){
            throw new ValidationException($"Duplicate action IDs found: {string.Join(", ", duplicateActionIds)}");
        }

        // Check for exactly one initial state
        var initialStates = request.States.Where(s => s.IsInitial).ToList();
        if (initialStates.Count != 1){
            throw new ValidationException("Workflow must have exactly one initial state");
        }

        // Validate state IDs are not empty
        var emptyStateIds = request.States.Where(s => string.IsNullOrWhiteSpace(s.Id)).ToList();
        if (emptyStateIds.Any()){
            throw new ValidationException("All states must have non-empty IDs");
        }

        // Validate action IDs are not empty
        var emptyActionIds = request.Actions.Where(a => string.IsNullOrWhiteSpace(a.Id)).ToList();
        if (emptyActionIds.Any()){
            throw new ValidationException("All actions must have non-empty IDs");
        }

        // Validate actions reference valid states
        var validStateIds = new HashSet<string>(stateIds);
        foreach (var action in request.Actions){
            if (!validStateIds.Contains(action.ToState)){
                throw new ValidationException($"Action '{action.Id}' references invalid target state '{action.ToState}'");
            }

            foreach (var fromState in action.FromStates){
                if (!validStateIds.Contains(fromState)){
                    throw new ValidationException($"Action '{action.Id}' references invalid source state '{fromState}'");
                }
            }

            if (action.FromStates.Count == 0){
                throw new ValidationException($"Action '{action.Id}' must have at least one source state");
            }
        }
    }
}