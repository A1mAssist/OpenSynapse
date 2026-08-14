// Resolve x64 MSVC CompleteObjectLocator/type-descriptor names for vftables.
// @category OpenSynapse

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class ExportMsvcRtti extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            throw new IllegalArgumentException("Usage: ExportMsvcRtti.java <output-file>");
        }
        File output = new File(args[0]);
        File parent = output.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            throw new IllegalStateException("Cannot create output directory: " + parent);
        }
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8)) {
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            int count = 0;
            while (symbols.hasNext() && !monitor.isCancelled()) {
                Symbol symbol = symbols.next();
                if (!symbol.getName().toLowerCase().contains("vftable") ||
                    symbol.getName().toLowerCase().contains("meta_ptr")) {
                    continue;
                }
                Address vf = symbol.getAddress();
                Address meta = vf.subtract(8);
                try {
                    long colValue = currentProgram.getMemory().getLong(meta);
                    Address col = currentProgram.getAddressFactory().getDefaultAddressSpace()
                        .getAddress(colValue);
                    int typeRva = currentProgram.getMemory().getInt(col.add(0x0c));
                    Address typeDesc = currentProgram.getImageBase().add(Integer.toUnsignedLong(typeRva));
                    String name = readCString(typeDesc.add(0x10), 256);
                    writer.printf("%s\t%s\t%s\t%s%n", vf, col, typeDesc, name);
                    count++;
                } catch (Exception ex) {
                    writer.printf("%s\t%s\tUNRESOLVED\t%s%n", vf, meta, ex.getMessage());
                }
            }
            println("Exported " + count + " MSVC RTTI vftables to " + output.getAbsolutePath());
        }
    }

    private String readCString(Address address, int max) throws Exception {
        StringBuilder value = new StringBuilder();
        for (int i = 0; i < max; i++) {
            byte b = currentProgram.getMemory().getByte(address.add(i));
            if (b == 0) {
                break;
            }
            value.append((char) (b & 0xff));
        }
        return value.toString();
    }
}
