using EGG9000.Bot.Automated.Coops;

using System;
using System.Collections.Generic;
using System.Reflection;

using Ei;

namespace EGG9000.Test.Coops {
    // Shared builders and reflection bridges for ThreadsCoopStatusUpdater tests. The updater keeps
    // several pure helpers private static; these tests exercise the shipped code as-is (no source
    // changes) so private members are reached by reflection.
    static internal class CoopUpdaterTestHelpers {
        private static readonly Type UpdaterType = typeof(ThreadsCoopStatusUpdater);

        static internal TReturn InvokePrivateStatic<TReturn>(string name, params object[] args) {
            var method = UpdaterType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(UpdaterType.FullName, name);
            try {
                return (TReturn)method.Invoke(null, args)!;
            } catch(TargetInvocationException tie) when(tie.InnerException is not null) {
                throw tie.InnerException;
            }
        }

        // One contributor row for GetTachyonAmount. buffRates become the buff_history; the last entry
        // is the one the deflector math reads.
        static internal ContractCoopStatusResponse.Types.ContributionInfo Contributor(string uuid, params double[] buffRates) {
            var info = new ContractCoopStatusResponse.Types.ContributionInfo { Uuid = uuid };
            foreach(var rate in buffRates) {
                info.BuffHistory.Add(new CoopBuffState { EggLayingRate = rate });
            }
            return info;
        }

        static internal List<ContractCoopStatusResponse.Types.ContributionInfo> Contributors(
            params ContractCoopStatusResponse.Types.ContributionInfo[] contributors) => [.. contributors];
    }
}
