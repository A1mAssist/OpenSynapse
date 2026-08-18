// Export an effect vftable, constructor references, and direct method decompilations.
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

public class ExportEffectByVftable extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) {
            throw new IllegalArgumentException(
                "Usage: ExportEffectByVftable.java <vftable-hex> <output> [helper-hex ...]");
        }

        Address vftable = toAddr(Long.parseUnsignedLong(stripHexPrefix(args[0]), 16));
        Set<Function> functions = new LinkedHashSet<>();
        StringBuilder output = new StringBuilder();
        output.append("# Effect direct static evidence\n\n");
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

        if (args.length > 2) {
            output.append("\n## Requested helpers\n\n");
            for (int index = 2; index < args.length; index++) {
                if (args[index].startsWith("data:")) {
                    appendData(output, args[index].substring(5));
                    continue;
                }
                Address address = toAddr(Long.parseUnsignedLong(stripHexPrefix(args[index]), 16));
                Function function = getFunctionAt(address);
                if (function == null) {
                    output.append("- missing function at `").append(address).append("`\n");
                } else {
                    functions.add(function);
                    output.append("- `").append(address).append("` `")
                        .append(function.getName()).append("`\n");
                }
            }
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

        Path path = Path.of(args[1]);
        Files.createDirectories(path.getParent());
        Files.writeString(path, output.toString(), StandardCharsets.UTF_8);
    }

    private String stripHexPrefix(String value) {
        return value.startsWith("0x") || value.startsWith("0X")
            ? value.substring(2)
            : value;
    }

    private void appendData(StringBuilder output, String value) throws Exception {
        Address address = toAddr(Long.parseUnsignedLong(stripHexPrefix(value), 16));
        byte[] bytes = new byte[16];
        currentProgram.getMemory().getBytes(address, bytes);
        StringBuilder hex = new StringBuilder();
        for (byte item : bytes) {
            hex.append(String.format("%02X", item & 0xff));
        }
        int intValue = currentProgram.getMemory().getInt(address);
        long longValue = currentProgram.getMemory().getLong(address);
        output.append("- data `").append(address).append("`: hex `")
            .append(hex).append("`, int32 `").append(intValue)
            .append("`, float32 `").append(Float.intBitsToFloat(intValue))
            .append("`, float64 `").append(Double.longBitsToDouble(longValue))
            .append("`\n");
    }
}
