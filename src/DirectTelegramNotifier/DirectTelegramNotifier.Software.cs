#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using CbiAccount = NinjaTrader.Cbi.Account;
using CbiConnection = NinjaTrader.Cbi.Connection;
using CbiExecution = NinjaTrader.Cbi.Execution;
using CbiInstrument = NinjaTrader.Cbi.Instrument;
using CbiOrder = NinjaTrader.Cbi.Order;
using CbiPosition = NinjaTrader.Cbi.Position;
using Dtn = NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore;
#endregion

// DirectTelegramNotifier: which NinjaTrader and Replikanto versions are installed, and a message when they change between two starts.
// Local only: nothing is looked up on the internet. One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Software versions

        private const int SoftwareGraceSec = 90;       // Replikanto may still be loading this long after startup

        private string knownSoftware = string.Empty;   // "nt=8.1.8.4;rep=1.7.1.0" as last seen; kept in the state file
        private string[] softwareOverride;             // the tests set { NinjaTrader version, Replikanto version }
        private bool softwareDone;

        // An empty string means "not found". Never throws.
        private void ProbeSoftware(out string nt, out string replikanto)
        {
            if (softwareOverride != null)
            {
                nt = softwareOverride[0];
                replikanto = softwareOverride[1];
                return;
            }
            nt = NinjaTraderVersion();
            replikanto = ReplikantoVersion();
        }

        private static string NinjaTraderVersion()
        {
            try
            {
                Type globals = typeof(NinjaTrader.Core.Globals);
                System.Reflection.PropertyInfo p = globals.GetProperty("AssemblyVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (p != null)
                {
                    object v = p.GetValue(null, null);
                    if (v != null && v.ToString().Length > 0)
                        return v.ToString();
                }
                string location = globals.Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                    return System.Diagnostics.FileVersionInfo.GetVersionInfo(location).FileVersion ?? string.Empty;
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }

        private static string ReplikantoVersion()
        {
            try
            {
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Reflection.AssemblyName name = a.GetName();
                    if (!string.Equals(name.Name, "Replikanto", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string location = null;
                    try { location = a.Location; } catch (Exception) { }
                    if (!string.IsNullOrEmpty(location))
                    {
                        string fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(location).FileVersion;
                        if (!string.IsNullOrEmpty(fileVersion))
                            return fileVersion;
                    }
                    return name.Version != null ? name.Version.ToString() : string.Empty;
                }
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }

        private static void SplitSoftware(string text, out string nt, out string replikanto)
        {
            nt = string.Empty;
            replikanto = string.Empty;
            if (string.IsNullOrEmpty(text))
                return;
            foreach (string part in text.Split(';'))
            {
                if (part.StartsWith("nt=", StringComparison.Ordinal))
                    nt = part.Substring(3);
                else if (part.StartsWith("rep=", StringComparison.Ordinal))
                    replikanto = part.Substring(4);
            }
        }

        // Once, after startup, from the watchdog tick. The first time it only remembers what it sees.
        private void RunSoftwareCheck()
        {
            if (softwareDone || !started)
                return;
            double age = (DateTime.UtcNow - startedUtc).TotalSeconds;
            if (age < 20)
                return;
            try
            {
                string nt, rep;
                ProbeSoftware(out nt, out rep);
                string before;
                lock (riskLock)
                    before = knownSoftware;
                string oldNt, oldRep;
                SplitSoftware(before, out oldNt, out oldRep);
                if (rep.Length == 0 && oldRep.Length > 0 && age < SoftwareGraceSec)
                    return;                                  // look again: Replikanto may not have loaded yet

                softwareDone = true;
                string now = "nt=" + nt + ";rep=" + rep;
                if (before.Length > 0 && before != now)
                    EnqueueText(MsgSoftwareChanged(oldNt, nt, oldRep, rep));
                lock (riskLock)
                {
                    if (knownSoftware != now)
                    {
                        knownSoftware = now;
                        riskStateDirty = true;
                    }
                }
            }
            catch (Exception ex)
            {
                softwareDone = true;
                WriteError("Software check", ex);
            }
        }

        private static string MsgSoftwareChanged(string oldNt, string nt, string oldRep, string rep)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(EmInfo).Append(" <b>SOFTWARE CHANGED</b>\n");
            if (oldNt != nt)
                sb.Append("NinjaTrader ").Append(oldNt.Length > 0 ? Html(oldNt) : "(not read)").Append(" \u2192 ").Append(nt.Length > 0 ? Html(nt) : "not read").Append("\n");
            if (oldRep != rep)
            {
                if (rep.Length == 0)
                    sb.Append("Replikanto not detected (was ").Append(Html(oldRep)).Append(")\n");
                else if (oldRep.Length == 0)
                    sb.Append("Replikanto ").Append(Html(rep)).Append(" detected\n");
                else
                    sb.Append("Replikanto ").Append(Html(oldRep)).Append(" \u2192 ").Append(Html(rep)).Append("\n");
            }
            sb.Append("Check that Replikanto and your strategies are running and that /status looks right.");
            return sb.ToString();
        }

        private string BuildVersionText()
        {
            string nt, rep;
            ProbeSoftware(out nt, out rep);
            StringBuilder sb = new StringBuilder();
            sb.Append(EmInfo).Append(" DirectTelegramNotifier v").Append(AddOnVersion);
            sb.Append("\nNinjaTrader ").Append(nt.Length > 0 ? Html(nt) : "version not readable");
            if (rep.Length > 0)
                sb.Append("\nReplikanto ").Append(Html(rep));
            return sb.ToString();
        }

        #endregion
    }
}
