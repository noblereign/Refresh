﻿using System.Text;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Refresh.Analyzers.SyntaxReceivers;

namespace Refresh.Analyzers;

[Generator]
public class ActivityGenerator : ISourceGenerator
{
    [LanguageInjection("csharp")]
    private const string Header =
        """
        // <auto-generated/>
        using Refresh.GameServer.Types.Activity;
        using Refresh.GameServer.Types.UserData;
        using Refresh.GameServer.Types.UserData.Leaderboard;
        using Refresh.GameServer.Types.Relations;
        using Refresh.GameServer.Types.Levels;
        """;
    
    public void Initialize(GeneratorInitializationContext context)
    {
        // no initialization code
    }

    /// <summary>
    /// Returns the type indicated by the input.
    /// Example: <c>Level_Upload</c> resolves to <c>GameLevel</c>
    /// </summary>
    private static string GetTypeFromName(string input)
    {
        string typeName = input.Substring(0, input.IndexOf('_'));

        if (typeName != "RateLevelRelation") return "Game" + typeName;
        return typeName;
    }

    private static (string, string) GetIdFieldFromName(string name)
    {
        string idField;
        string idFieldValue;

        name = name.Replace("Game", "");

        if (name != "User" && name != "Score" && name != "SubmittedScore" && name != "RateLevelRelation")
        {
            idField = "StoredSequentialId";
            idFieldValue = idField + ".Value";
        }
        else
        {
            idField = "StoredObjectId";
            idFieldValue = idField;
        }

        return (idField, idFieldValue);
    }

    private static void GenerateCreateEvents(GeneratorExecutionContext context, IEnumerable<string> names)
    {
        string code = string.Empty;
        foreach (string eventName in names)
        {
            string type = GetTypeFromName(eventName);
            string typeParam = type.ToLower();
            string typeId = type.Replace("Game", "").Replace("Submitted", "");
            
            (string idField, string _) = GetIdFieldFromName(type);
            
            string method = $@"
    /// <summary>
    /// Creates a new {eventName} event from a <see cref='{type}'/>, and adds it to the event list.
    /// </summary>
    public Event Create{eventName.Replace("_", string.Empty)}Event(GameUser userFrom, {type} {typeParam})
    {{
        Event @event = new();
        @event.EventType = EventType.{eventName};
        @event.StoredDataType = EventDataType.{typeId};
        @event.Timestamp = this.Time.TimestampMilliseconds;
        @event.User = userFrom;

        @event.{idField} = {typeParam}.{typeId}Id;

        this.Write(() => this.Add(@event));
        return @event;
    }}
";

            code += method;
        }


        string sourceCode = $@"{Header}
namespace Refresh.GameServer.Database;

public partial interface IGameDatabaseContext
{{
{code}
}}";
        
        context.AddSource("RealmDatabaseContext.Activity.CreateEvents.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }

    private static void GenerateDataGetters(GeneratorExecutionContext context, IEnumerable<string> names)
    {
        string code = string.Empty;
        
        foreach (string name in names)
        {
            string type = "Game" + name;
            if (name == "RateLevelRelation") type = name;

            (string idField, string idFieldValue) = GetIdFieldFromName(name);

            string idAccess = name + "Id";
            if (name == "GameSubmittedScore" || type == "GameScore")
            {
                idAccess = "ScoreId";
                type = "GameSubmittedScore";
            }

            string method = $@"
    public {type}? Get{name}FromEvent(Event @event)
    {{
        if (@event.StoredDataType != EventDataType.{name})
            throw new InvalidOperationException(""Event does not store the correct data type (expected {name})"");

        if (@event.{idField} == null)
            throw new InvalidOperationException(""Event was not created correctly, expected {idField} to not be null"");

        return this.All<{type}>()
            .FirstOrDefault(l => l.{idAccess} == @event.{idFieldValue});
    }}
";

            code += method;
        }
        
        string sourceCode = $@"{Header}
namespace Refresh.GameServer.Database;

#nullable enable

public partial interface IGameDatabaseContext
{{
{code}
}}";
        
        context.AddSource("IGameDatabaseContext.Activity.DataGetters.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }

    public void Execute(GeneratorExecutionContext context)
    {
        EnumNameReceiver syntaxReceiver = new();
        
        foreach (SyntaxTree tree in context.Compilation.SyntaxTrees)
            syntaxReceiver.OnVisitSyntaxNode(tree.GetRoot());
        
        
        foreach ((string className, List<string> names) in syntaxReceiver.Enums)
            switch (className)
            {
                case "EventType":
                    GenerateCreateEvents(context, names);
                    break;
                case "EventDataType":
                    GenerateDataGetters(context, names);
                    break;
            }
    }
}