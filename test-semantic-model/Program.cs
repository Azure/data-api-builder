using Microsoft.AnalysisServices.AdomdClient;
using var conn = new AdomdConnection("Data Source=localhost:60488");
conn.Open();

// Build ID→Name maps
var tableIdToName = new Dictionary<long, string>();
var columnIdToName = new Dictionary<long, string>();
var columnIdToTableId = new Dictionary<long, long>();

using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_TABLES";
    using var r = cmd.ExecuteReader();
    int idOrd = -1, nameOrd = -1;
    for (int i = 0; i < r.FieldCount; i++) {
        if (r.GetName(i) == "ID") idOrd = i;
        if (r.GetName(i) == "Name") nameOrd = i;
    }
    while (r.Read()) {
        tableIdToName[Convert.ToInt64(r.GetValue(idOrd))] = r.GetValue(nameOrd)?.ToString() ?? "";
    }
}

using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_COLUMNS";
    using var r = cmd.ExecuteReader();
    int idOrd = -1, nameOrd = -1, tableIdOrd = -1;
    for (int i = 0; i < r.FieldCount; i++) {
        if (r.GetName(i) == "ID") idOrd = i;
        if (r.GetName(i) == "ExplicitName") nameOrd = i;
        if (r.GetName(i) == "TableID") tableIdOrd = i;
    }
    while (r.Read()) {
        var colId = Convert.ToInt64(r.GetValue(idOrd));
        columnIdToName[colId] = r.GetValue(nameOrd)?.ToString() ?? "";
        columnIdToTableId[colId] = Convert.ToInt64(r.GetValue(tableIdOrd));
    }
}

Console.WriteLine("=== RELATIONSHIPS (resolved) ===");
Console.WriteLine($"{"From Table",-20} {"From Column",-20} {"Card",-5} {"To Table",-20} {"To Column",-20} {"Active",-7}");
Console.WriteLine(new string('-', 95));
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_RELATIONSHIPS";
    using var r = cmd.ExecuteReader();
    int fromTblOrd = -1, fromColOrd = -1, toTblOrd = -1, toColOrd = -1, fromCardOrd = -1, toCardOrd = -1, activeOrd = -1;
    for (int i = 0; i < r.FieldCount; i++) {
        var n = r.GetName(i);
        if (n == "FromTableID") fromTblOrd = i;
        if (n == "FromColumnID") fromColOrd = i;
        if (n == "ToTableID") toTblOrd = i;
        if (n == "ToColumnID") toColOrd = i;
        if (n == "FromCardinality") fromCardOrd = i;
        if (n == "ToCardinality") toCardOrd = i;
        if (n == "IsActive") activeOrd = i;
    }
    while (r.Read()) {
        var fromTbl = tableIdToName.GetValueOrDefault(Convert.ToInt64(r.GetValue(fromTblOrd)), "?");
        var fromCol = columnIdToName.GetValueOrDefault(Convert.ToInt64(r.GetValue(fromColOrd)), "?");
        var toTbl = tableIdToName.GetValueOrDefault(Convert.ToInt64(r.GetValue(toTblOrd)), "?");
        var toCol = columnIdToName.GetValueOrDefault(Convert.ToInt64(r.GetValue(toColOrd)), "?");
        var fromCard = r.GetValue(fromCardOrd);
        var toCard = r.GetValue(toCardOrd);
        var active = r.GetValue(activeOrd);
        Console.WriteLine($"{fromTbl,-20} {fromCol,-20} {fromCard}→{toCard}  {toTbl,-20} {toCol,-20} {active}");
    }
}

Console.WriteLine("\n=== MEASURES (resolved) ===");
Console.WriteLine($"{"Table",-20} {"Measure",-30} {"Type",-6} {"Expression"}");
Console.WriteLine(new string('-', 120));
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_MEASURES";
    using var r = cmd.ExecuteReader();
    int tblIdOrd = -1, nameOrd = -1, exprOrd = -1, dtOrd = -1, hiddenOrd = -1;
    for (int i = 0; i < r.FieldCount; i++) {
        var n = r.GetName(i);
        if (n == "TableID") tblIdOrd = i;
        if (n == "Name") nameOrd = i;
        if (n == "Expression") exprOrd = i;
        if (n == "DataType") dtOrd = i;
        if (n == "IsHidden") hiddenOrd = i;
    }
    while (r.Read()) {
        var tbl = tableIdToName.GetValueOrDefault(Convert.ToInt64(r.GetValue(tblIdOrd)), "?");
        var name = r.GetValue(nameOrd)?.ToString() ?? "";
        var expr = r.GetValue(exprOrd)?.ToString()?.Replace("\n", " ").Replace("\r", "") ?? "";
        if (expr.Length > 60) expr = expr[..60] + "...";
        var dt = r.GetValue(dtOrd);
        var hidden = r.GetValue(hiddenOrd);
        if (hidden?.ToString() == "False")
            Console.WriteLine($"{tbl,-20} {name,-30} {dt,-6} {expr}");
    }
}
