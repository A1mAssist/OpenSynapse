// Dump small constant blocks referenced by the effect initializers.
//@category OpenSynapse
import ghidra.program.model.address.Address;
import java.nio.file.Files;
import java.nio.file.Path;

public class DumpLightingConstants extends ghidra.app.script.GhidraScript {
    @Override
    public void run() throws Exception {
        long[][] blocks = {
            {0x1801ac640L, 0x80},
            {0x1801ac8c0L, 0x80},
            {0x1801ac9c0L, 0x80},
            {0x1801a9680L, 0x80},
            {0x1801aade0L, 0x40},
            {0x1801f5330L, 0x100}
        };
        StringBuilder out = new StringBuilder();
        for (long[] block : blocks) {
            Address address = toAddr(block[0]);
            byte[] bytes = new byte[(int) block[1]];
            currentProgram.getMemory().getBytes(address, bytes);
            out.append(String.format("## `%s` (%d bytes)%n%n```text%n", address, bytes.length));
            for (int offset = 0; offset < bytes.length; offset += 16) {
                out.append(String.format("%s: ", address.add(offset)));
                for (int i = 0; i < 16 && offset + i < bytes.length; i++) {
                    out.append(String.format("%02X ", bytes[offset + i] & 0xff));
                }
                out.append('\n');
            }
            out.append("```\n\n");
        }
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected output path");
        }
        Files.writeString(Path.of(args[0]), out.toString());
    }
}
