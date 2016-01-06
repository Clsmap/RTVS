using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.R.Core.AST;
using Microsoft.R.Editor.Signatures;
using Microsoft.VisualStudio.Language.Intellisense;

namespace Microsoft.R.Editor.Test.Utility {
    internal static class SignatureHelpSourceUtility {
        internal static Task AugmentSignatureHelpSessionAsync(this SignatureHelpSource signatureHelpSource, ISignatureHelpSession session, IList<ISignature> signatures, AstRoot ast) {
            var tcs = new TaskCompletionSource<object>();

            var ready = signatureHelpSource.AugmentSignatureHelpSession(session, signatures, ast, o => {
                signatureHelpSource.AugmentSignatureHelpSession(session, signatures, ast, null);
                tcs.TrySetResult(null);
            });

            if (ready) {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }
    }
}