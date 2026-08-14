// Decompile the shared renderer/helper boundaries identified by the first pass.
//@category OpenSynapse
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.util.task.ConsoleTaskMonitor;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

public class DecompileLightingHelpers extends ghidra.app.script.GhidraScript {
    @Override
    public void run() throws Exception {
        String[] names = {
            "FUN_1800692e0", "FUN_180069590", "FUN_180065f50", "FUN_180066160",
            "FUN_180069600", "FUN_180069140", "FUN_180069190", "FUN_18004b6c0",
            "FUN_1801551d0", "FUN_180194710", "FUN_180194db0"
        };
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        StringBuilder out = new StringBuilder();
        for (String name : names) {
            List<Function> functions = getGlobalFunctions(name);
            Function function = functions.isEmpty() ? null : functions.get(0);
            if (function == null) {
                out.append("## ").append(name).append("\n\nMISSING\n\n");
                continue;
            }
            DecompileResults result = decompiler.decompileFunction(function, 60, new ConsoleTaskMonitor());
            out.append("## ").append(name).append(" at `").append(function.getEntryPoint()).append("`\n\n");
            out.append("```c\n").append(result.getDecompiledFunction().getC()).append("\n```\n\n");
        }
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected output path");
        }
        Files.writeString(Path.of(args[0]), out.toString());
        decompiler.dispose();
    }
}
