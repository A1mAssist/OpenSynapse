// Decompile one or more image-relative virtual addresses.
// @category OpenSynapse

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DecompileRvas extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length == 0) {
            throw new IllegalArgumentException("expected one or more hexadecimal RVAs");
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (String arg : args) {
                long rva = Long.parseUnsignedLong(arg.replaceFirst("^(?i)0x", ""), 16);
                Address address = currentProgram.getImageBase().add(rva);
                Function function = getFunctionContaining(address);
                if (function == null) {
                    println("FUNCTION_NOT_FOUND RVA 0x" + Long.toHexString(rva) + " " + address);
                    continue;
                }

                println("FUNCTION " + function.getName() + " " + function.getEntryPoint() +
                    " requested RVA 0x" + Long.toHexString(rva));
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted()) {
                    println(result.getDecompiledFunction().getC());
                } else {
                    println("DECOMPILE_FAILED " + result.getErrorMessage());
                }
            }
        } finally {
            decompiler.dispose();
        }
    }
}
