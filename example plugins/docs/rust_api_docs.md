
# Rust API Documentation

This file contains a combination of descriptions and code examples for the Rust API, extracted from the uMod documentation.

## Server Hooks

### OnServerInitialized
- Called after the server startup has been completed and is awaiting connections
- Also called for plugins that are hotloaded while the server is already started running
- Boolean parameter, false if called on plugin hotload and true if called on server initialization
- No return behavior
```csharp
void OnServerInitialized(bool initial)
{
    Puts("OnServerInitialized works!");
}
```

### Init
- Called when a plugin is being initialized
- Other plugins may or may not be present, dependant on load order
- No return behavior
```csharp
void Init()
{
    Puts("Init works!");
}
```

### OnServerRestartInterrupt
- Called when a server restart is being cancelled
- Returning a non-null value overrides default behavior
```csharp
object OnServerRestartInterrupt()
{
    Puts("OnServerRestartInterrupt works!");
    return null;
}
```

### OnServerShutdown
- Useful for saving something / etc on server shutdown
- No return behavior
```csharp
void OnServerShutdown()
{
    Puts("OnServerShutdown works!");
}
```

### OnServerCommand
- Useful for intercepting commands before they get to their intended target
- Returning a non-null value overrides default behavior
```csharp
object OnServerCommand(ConsoleSystem.Arg arg)
{
    Puts("OnServerCommand works!");
    return null;
}
```

## Player Hooks

### CanAffordUpgrade
- Called when the resources for an upgrade are checked
- Returning true or false overrides default behavior
```csharp
bool CanAffordUpgrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade)
{
    Puts("CanAffordUpgrade works!");
    return true;
}
```

### CanAssignBed
- Called when a player attempts to assign a bed or sleeping bag to another player
- Returning a non-null value overrides default behavior
```csharp
object CanAssignBed(BasePlayer player, SleepingBag bag, ulong targetPlayerId)
{
    Puts("CanAssignBed works!");
    return null;
}
```

### CanUpdateSign
- Called when the player attempts to change the text on a sign or lock it, or update a photo frame
- Returning true or false overrides default behavior
```csharp
bool CanUpdateSign(BasePlayer player, Signage sign)
{
    Puts("CanUpdateSign works!");
    return true;
}

bool CanUpdateSign(BasePlayer player, PhotoFrame photoFrame)
{
    Puts("CanUpdateSign works!");
    return true;
}
```

### OnUserChat
- Called when a player sends a chat message to the server
- Returning true overrides default behavior of chat, not commands
```csharp
object OnUserChat(IPlayer player, string message)
{
    Puts($"{player.Name} said: {message}");
    return null;
}
```

## Entity Hooks

### CanBradleyApcTarget
- Called when an APC targets an entity
- Returning true or false overrides default behavior
```csharp
bool CanBradleyApcTarget(BradleyAPC apc, BaseEntity entity)
{
    Puts("CanBradleyApcTarget works!");
    return true;
}
```

### OnNpcPlayerResume
- Useful for canceling the invoke of TryForceToNavmesh
- Returning a non-null value cancels default behavior
```csharp
object OnNpcPlayerResume(NPCPlayerApex npc)
{
    Puts("OnNpcPlayerResume works!");
    return null;
}
```
