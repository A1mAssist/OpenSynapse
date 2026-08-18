// Export a function's instruction listing for decompiler cross-checks.
//@category OpenSynapse

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

public class ExportFunctionListing extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) {
            throw new IllegalArgumentException(
                "Usage: ExportFunctionListing.java <function-hex> <output>");
        }
        String raw = args[0].startsWith("0x") ? args[0].substring(2) : args[0];
        Address address = toAddr(Long.parseUnsignedLong(raw, 16));
        Function function = getFunctionAt(address);
        if (function == null) {
            throw new IllegalArgumentException("No function at " + address);
        }

        StringBuilder output = new StringBuilder();
        output.append("# ").append(function.getName()).append(" at `")
            .append(address).append("`\n\n```asm\n");
        InstructionIterator instructions = currentProgram.getListing()
            .getInstructions(function.getBody(), true);
        while (instructions.hasNext() && !monitor.isCancelled()) {
            Instruction instruction = instructions.next();
            output.append(instruction.getAddress()).append("  ")
                .append(instruction.toString()).append('\n');
        }
        output.append("```\n");

        Path path = Path.of(args[1]);
        Files.createDirectories(path.getParent());
        Files.writeString(path, output.toString(), StandardCharsets.UTF_8);
    }
}
