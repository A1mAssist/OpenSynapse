// Export CTidalEffect vtable, constructor references, and direct method decompilations.
//@category OpenSynapse

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.util.task.ConsoleTaskMonitor;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashSet;
import java.util.Set;

public class ExportTidalEffect extends GhidraScript {
    private static final long TIDAL_VFTABLE = 0x1801acc48L;
    private static final long[] TIDAL_HELPERS = {
        0x180067830L, 0x180067960L, 0x180067e50L, 0x180067f40L,
        0x1800680f0L, 0x180068300L
    };
    private static final long[] TIDAL_CONSTANTS = {
        0x1801ac680L, 0x1801ac688L, 0x1801acb08L, 0x1801acbd0L,
        0x1801acbd8L, 0x1801acbe0L, 0x1801acbe4L, 0x1801acbe8L,
        0x1801acbecL, 0x1801acbf0L, 0x1801acc00L, 0x1801acc10L,
        0x1801acc20L, 0x1801acc30L, 0x1801a93f0L, 0x1801a9690L,
        0x1801a9a60L, 0x1801a9a70L, 0x1801aca10L, 0x1801aadd8L
    };

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected output path");
        }

        Address vftable = toAddr(TIDAL_VFTABLE);
        Set<Function> functions = new LinkedHashSet<>();
        StringBuilder output = new StringBuilder();
        output.append("# CTidalEffect direct static evidence\n\n");
        output.append("Vftable: `").append(vftable).append("`\n\n");
        output.append("## Vftable slots\n\n");

        for (int slot = 0; slot < 32; slot++) {
            long pointer = currentProgram.getMemory().getLong(vftable.add(slot * 8L));
            if (pointer == 0) {
                break;
            }
            Address target = toAddr(pointer);
            Function function = getFunctionAt(target);
            if (function == null) {
                function = getFunctionContaining(target);
            }
            if (function == null || function.isExternal()) {
                break;
            }
            functions.add(function);
            output.append("- slot ").append(slot).append(": `")
                .append(target).append("` `").append(function.getName())
                .append("`, size `0x").append(Long.toHexString(function.getBody().getNumAddresses()))
                .append("`\n");
        }

        output.append("\n## Vftable references\n\n");
        for (Reference reference : getReferencesTo(vftable)) {
            Function function = getFunctionContaining(reference.getFromAddress());
            output.append("- `").append(reference.getFromAddress()).append("` in `")
                .append(function == null ? "<none>" : function.getName()).append("`\n");
            if (function != null) {
                functions.add(function);
            }
        }

        output.append("\n## Effect-specific helpers\n\n");
        for (long helperAddress : TIDAL_HELPERS) {
            Function function = getFunctionAt(toAddr(helperAddress));
            if (function == null) {
                output.append("- missing function at `")
                    .append(toAddr(helperAddress)).append("`\n");
            } else {
                functions.add(function);
                output.append("- `").append(function.getEntryPoint()).append("` `")
                    .append(function.getName()).append("`, size `0x")
                    .append(Long.toHexString(function.getBody().getNumAddresses()))
                    .append("`\n");
            }
        }

        output.append("\n## Referenced constants\n\n");
        for (long constantAddress : TIDAL_CONSTANTS) {
            Address address = toAddr(constantAddress);
            byte[] bytes = new byte[16];
            currentProgram.getMemory().getBytes(address, bytes);
            output.append("- `").append(address).append("`: hex `")
                .append(toHex(bytes)).append("`, int32 `")
                .append(currentProgram.getMemory().getInt(address)).append("`, float32 `")
                .append(Float.intBitsToFloat(currentProgram.getMemory().getInt(address)))
                .append("`, float64 `")
                .append(Double.longBitsToDouble(currentProgram.getMemory().getLong(address)))
                .append("`\n");
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        for (Function function : functions) {
            DecompileResults result = decompiler.decompileFunction(
                function, 120, new ConsoleTaskMonitor());
            output.append("\n## ").append(function.getName()).append(" at `")
                .append(function.getEntryPoint()).append("`\n\n```c\n");
            if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                output.append(result.getDecompiledFunction().getC());
            } else {
                output.append("DECOMPILE FAILED: ").append(result.getErrorMessage());
            }
            output.append("\n```\n");
        }
        decompiler.dispose();

        Path path = Path.of(args[0]);
        Files.createDirectories(path.getParent());
        Files.writeString(path, output.toString(), StandardCharsets.UTF_8);
    }

    private String toHex(byte[] bytes) {
        StringBuilder value = new StringBuilder();
        for (byte item : bytes) {
            value.append(String.format("%02X", item & 0xff));
        }
        return value.toString();
    }
}
