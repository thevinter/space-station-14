﻿using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.AutoGenerated;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Players;

namespace Content.Client.Administration.UI.CustomControls
{
    [GenerateTypedNameReferences]
    public partial class PlayerListControl : BoxContainer
    {
        private List<ICommonSession>? _data;

        public event Action<ICommonSession?>? OnSelectionChanged;

        protected override void EnteredTree()
        {
            // Fill the Option data
            _data = IoCManager.Resolve<IPlayerManager>().Sessions.OfType<ICommonSession>().ToList();
            PopulateList();
            PlayerItemList.OnItemSelected += PlayerItemListOnOnItemSelected;
            PlayerItemList.OnItemDeselected += PlayerItemListOnOnItemDeselected;
            FilterLineEdit.OnTextChanged += FilterLineEditOnOnTextEntered;
        }

        private void FilterLineEditOnOnTextEntered(LineEdit.LineEditEventArgs obj)
        {
            PopulateList(FilterLineEdit.Text);
        }

        private static string GetDisplayName(ICommonSession session)
        {
            return $"{session.Name} ({session.AttachedEntity?.Name})";
        }

        private void PlayerItemListOnOnItemSelected(ItemList.ItemListSelectedEventArgs obj)
        {
            var selectedPlayer = (ICommonSession) obj.ItemList[obj.ItemIndex].Metadata!;
            OnSelectionChanged?.Invoke(selectedPlayer);
        }

        private void PlayerItemListOnOnItemDeselected(ItemList.ItemListDeselectedEventArgs obj)
        {
            OnSelectionChanged?.Invoke(null);
        }

        private void PopulateList(string? filter = null)
        {
            // _data should never be null here
            if (_data == null)
                return;
            PlayerItemList.Clear();
            foreach (var session in _data)
            {
                var displayName = GetDisplayName(session);
                if (!string.IsNullOrEmpty(filter) &&
                    !displayName.ToLowerInvariant().Contains(filter.Trim().ToLowerInvariant()))
                {
                    continue;
                }

                PlayerItemList.Add(new ItemList.Item(PlayerItemList)
                {
                    Metadata = session,
                    Text = displayName
                });
            }
        }

        public void ClearSelection()
        {
            PlayerItemList.ClearSelected();
        }
    }
}
