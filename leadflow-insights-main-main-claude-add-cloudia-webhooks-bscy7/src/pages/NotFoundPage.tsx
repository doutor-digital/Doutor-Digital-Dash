import { Link } from "react-router-dom";
import { Ghost } from "lucide-react";
import { Button } from "@/components/ui/Button";

export default function NotFoundPage() {
  return (
    <div className="flex flex-col items-center justify-center text-center py-20">
      <Ghost className="h-14 w-14 text-slate-500 mb-3" />
      <h1 className="text-2xl font-semibold">Página não encontrada</h1>
      <p className="text-sm text-slate-400 mt-1 max-w-md">
        O recurso que você tentou acessar não existe ou foi movido.
      </p>
      <Link to="/" className="mt-6">
        <Button>Voltar ao dashboard</Button>
      </Link>
    </div>
  );
}
