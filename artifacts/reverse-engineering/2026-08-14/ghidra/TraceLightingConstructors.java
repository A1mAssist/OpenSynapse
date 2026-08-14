// Find code references that install effect vftables and decompile their containing functions.
//@category OpenSynapse
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.util.task.ConsoleTaskMonitor;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashSet;
import java.util.Set;

public class TraceLightingConstructors extends ghidra.app.script.GhidraScript {
    @Override
    public void run() throws Exception {
        long[] vftables = {
            0x1801ac5a8L, 0x1801ac698L, 0x1801ac758L, 0x1801ac928L, 0x1801aca28L
        };
        Set<Function> functions = new LinkedHashSet<>();
        StringBuilder out = new StringBuilder();
        for (long value : vftables) {
            Address target = toAddr(value);
            out.append("## vftable `").append(target).append("`\n\n");
            for (Reference reference : getReferencesTo(target)) {
                Function function = getFunctionContaining(reference.getFromAddress());
                out.append("- `").append(reference.getFromAddress()).append("` in `")
                    .append(function == null ? "<none>" : function.getName()).append("`\n");
                if (function != null) {
                    functions.add(function);
                }
            }
            out.append('\n');
        }
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        for (Function function : functions) {
            DecompileResults result = decompiler.decompileFunction(function, 60, new ConsoleTaskMonitor());
            out.append("## ").append(function.getName()).append(" at `")
                .append(function.getEntryPoint()).append("`\n\n```c\n")
                .append(result.getDecompiledFunction().getC()).append("\n```\n\n");
        }
        decompiler.dispose();
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected output path");
        }
        Files.writeString(Path.of(args[0]), out.toString());
    }
}
