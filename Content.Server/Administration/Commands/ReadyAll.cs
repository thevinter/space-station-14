﻿using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Server)]
    public class ReadyAll : IConsoleCommand
    {
        public string Command => "readyall";
        public string Description => "Readies up all players in the lobby, except for observers.";
        public string Help => $"{Command} | ̣{Command} <ready>";
        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var ready = true;

            if (args.Length > 0)
            {
                ready = bool.Parse(args[0]);
            }

            var gameTicker = EntitySystem.Get<GameTicker>();


            if (gameTicker.RunLevel != GameRunLevel.PreRoundLobby)
            {
                shell.WriteLine("This command can only be ran while in the lobby!");
                return;
            }

            foreach (var (player, status) in gameTicker.PlayersInLobby)
            {
                if(status != LobbyPlayerStatus.Observer)
                    gameTicker.ToggleReady(player, ready);
            }
        }
    }
}
