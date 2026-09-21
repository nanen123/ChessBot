using ChessBot.Chess.Application;

namespace ChessBot.Agents
{
    public interface IChessAgentHost
    {
        ChessGameController Game { get; }
        int MaximumPlies { get; }
        void AgentReady(ChessAgent agent);
        void Submit(ChessAgent agent, int action);
    }
}
