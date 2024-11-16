using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Localize;
using BattleTech;
using BattleTech.Data;
using BattleTech.UI;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    public enum ExitType {
        Contract,
        Conversation,
        Event,
        Exit,
        Node
    }
/*
    internal enum ConvCastIds {
        Sumire = "598cd3a06230355c18000067",
        Darius = "598cd3f26230355c18000069",
        Yang = "598cd3f36230355c1800006a",
        Farah = "59b98a97623035ac140056c6",
        Kamea = "59baac916230356416000354",
        Alexander = "59b97f45623035ac140054ca",
        Viewscreen = "59f8f0296230359c2100082c",
    }*/

    public class Option {
        ExitType to;
        string _goto;

        public void validate(string nodeKey, string optionKey, Dictionary<string, EConvNode> nodes) {
            string[] parts = _goto.Split(':');

            switch (parts[0]) {
                case "Contract": {
                    to = ExitType.Contract;
                    _goto = parts[1];

                    if (MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = _goto }).ToArray().Length == 0) {
                        throw new Exception($"VALIDATION: Node {nodeKey}[{optionKey}] points to Contract '{_goto}', but it does not exist.");
                    }
                    break;
                }
                case "Conversation": {
                    to = ExitType.Conversation;
                    _goto = parts[1];
                    break;
                }
                case "Event": {
                    to = ExitType.Event;
                    _goto = parts[1];

                    if (MetadataDatabase.Instance.GetEventDef(_goto) == null) {
                        throw new Exception($"VALIDATION: Node {nodeKey}[{optionKey}] points to Event '{_goto}', but it does not exist.");
                    }
                    break;
                }
                case "Exit": {
                    to = ExitType.Exit;
                    _goto = "";
                    break;
                }
                case "Node": {
                    to = ExitType.Node;
                    _goto = parts[1];
                    if (!nodes.ContainsKey(_goto)) {
                        throw new Exception($"VALIDATION: Node {nodeKey}[{optionKey}] points to Node '{_goto}', but it does not exist.");
                    }
                    break;
                }

                default:
                    throw new Exception($"VALIDATION: Node {nodeKey}[{optionKey}] didn't make sense: '{_goto}'.");
            }
        }
    }

    public class EConvNode {
        public string playSound;
        public string showOnViewscreen;
        public string speaker = "";
        public string text = "";
        public Dictionary<string, Option> options;

        public void validate(string nodeKey, Dictionary<string, EConvNode> nodes) {
            if (String.IsNullOrEmpty(speaker)) {
                throw new Exception($"VALIDATION: Node {nodeKey} is missing 'speaker'");
            }

            if (String.IsNullOrEmpty(text)) {
                throw new Exception($"VALIDATION: Node {nodeKey} is missing 'text'");
            }

            if (options != null) {
                throw new Exception($"VALIDATION: Node {nodeKey} is missing 'options'");
            }

            foreach (string optionKey in options.Keys) {
                options[optionKey].validate(nodeKey, optionKey, nodes);
            }

            if (options.ContainsKey("") && options.Count != 1) {
                throw new Exception($"VALIDATION: Node {nodeKey} has options[''], but more than one entry; to use the empty string option for 'Continue', it must be the only entry in 'options'.");
            }
        }
    }

    public class ExtendedConversation {
        public string name;
        public string introHeader;
        public string introSubHeader = "";
        public Dictionary<string, EConvNode> nodes = new Dictionary<string, EConvNode>();

        public void validate() {
            if (String.IsNullOrEmpty(name)) {
                throw new Exception($"VALIDATION: ExtendedConversation without a 'name'. It won't work at all.");
            }

            if (String.IsNullOrEmpty(introHeader)) {
                throw new Exception($"VALIDATION: ExtendedConversation is missing an 'introHeader'.");
            }

            foreach (string nodeKey in nodes.Keys) {
                nodes[nodeKey].validate(nodeKey, nodes);
            }
        }
    }
}
