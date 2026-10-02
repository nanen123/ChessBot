using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using ChessBot.Chess.Core;
using ChessBot.Training;
public static class ExportGeneratedCaptures
{
    public static void Main(string[] args)
    {
        var excluded=File.ReadAllLines(args[1]); var generator=new ChessCaptureGenerator(20261002,excluded);
        foreach(var line in File.ReadLines(args[0]))
        {
            var v=line.Split('\t');var board=BoardState.FromFen(v[2]);var type=(PieceType)Enum.Parse(typeof(PieceType),v[1],true);
            var timer=Stopwatch.StartNew();bool ok=generator.TryGenerate(board,int.Parse(v[0]),type,12,out var generated);timer.Stop();
            string moves=ok?string.Join(",",ChessRules.LegalMoves(generated).Where(m=>ChessTacticalAssessment.FavorableCapture(generated,m)).Select(m=>m.ToString())):"";
            Console.WriteLine(line+"\t"+generator.LastAttempts+"\t"+timer.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\t"+(ok?generated.ToFen():"")+"\t"+(ok?ChessCaptureGenerator.Identity(generated):"")+"\t"+moves);
        }
    }
}
