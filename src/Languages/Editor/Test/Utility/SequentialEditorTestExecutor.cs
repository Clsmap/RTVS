﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Languages.Editor.Shell;

namespace Microsoft.Languages.Editor.Test.Utility {
    [ExcludeFromCodeCoverage]
    public static class SequentialEditorTestExecutor {
        private static readonly object _creatorLock = new object();
        private static Action _disposeAction;

        public static void ExecuteTest(Action<ManualResetEventSlim> action) {
            ExecuteTest(action, null, null);
        }

        public static void ExecuteTest(Action<ManualResetEventSlim> action, Action initAction, Action disposeAction) {
            lock (_creatorLock) {
                _disposeAction = disposeAction;
                AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;

                if (initAction != null) {
                    initAction();
                }

                using (var evt = new ManualResetEventSlim()) {
                    action(evt);
                    evt.Wait();
                }
            }
        }

        private static void CurrentDomain_DomainUnload(object sender, EventArgs e) {
            if (_disposeAction != null) {
                _disposeAction();
                _disposeAction = null;
            }
        }
    }
}
